using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickMCP.Authentication;
using QuickMCP.Helpers;
using QuickMCP.Types;
using GenerativeAI;
using GenerativeAI.Types;
using GenerativeAI.Utility;
using Spectre.Console;
using Spectre.Console.Cli;

namespace QuickMCP.CLI.Commands.Build;

[Description("Build MCP Server configurations from an OpenAPI or Google Discovery specification.")]
public class BuildConfigCommand : AsyncCommand<BuildConfigCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, BuildConfigCommandSettings settings)
    {
        AnsiConsole.MarkupLine("[bold]Building MCP Server configuration[/]");
        var config = settings.GetBuilderConfig();

        AnsiConsole.MarkupLine("[bold yellow]Starting metadata generation...[/]");
        
        var mcpServerInfo = await QuickMCP.Builders.McpServerInfoBuilder.FromConfig(config).BuildAsync();
        AnsiConsole.MarkupLine($"[bold green]MCP Server info initialized with {mcpServerInfo.Tools.Count} tools[/]");

        if(mcpServerInfo.Tools.Count == 0)
            throw new Exception("No tools found in the specification");
        config = mcpServerInfo.BuilderConfig;
        var prefix = StringHelpers.SanitizeServerName(config.ServerName) ??
                     "mcp_server_" + DateTime.Now.Ticks.ToString();

        var outputPath = settings.OutputPath ??
                         Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), prefix));

        if (settings.AiMetadata == true)
        {
            var metadataFile = await BuildAndSaveMetadata(mcpServerInfo,settings, config, outputPath, prefix);
            config.MetadataFile = metadataFile;
        }

        var jsonOption = new JsonSerializerOptions(QuickMcpJsonSerializerContext.Default.Options);
        jsonOption.WriteIndented = true;

        //Build Local Caches
        await ConfigureAuth(settings, config);
        AnsiConsole.MarkupLine("[bold yellow]Writing local configuration...[/]");
        await WriteLocalAsync(config, outputPath, jsonOption, prefix);
        AnsiConsole.MarkupLine($"[bold green]MCP Server configuration successfully built![/]");
        return 0;
    }

    private async Task WriteLocalAsync(BuilderConfig config, string outputPath, JsonSerializerOptions jsonOption,
        string? prefix = null)
    {
        if (!Directory.Exists(outputPath))
        {
            AnsiConsole.MarkupLine("[bold yellow]Creating output directory...[/]");
            Directory.CreateDirectory(outputPath);
        }

        var configFile = Path.Combine(outputPath, $"{prefix}_config.json");

        // Download and save specs
        if (config.ApiSpecUrl != null)
        {
            AnsiConsole.MarkupLine("[bold yellow]Downloading API specifications...[/]");
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "QuickMCP");
            var specs = await client.GetStringAsync(config.ApiSpecUrl);

            string extension = ".json";
            if (config.ApiSpecUrl.EndsWith(".yaml"))
            {
                extension = ".yaml";
            }
            var specFile = Path.Combine(outputPath,
                $"{prefix}_api_specs{extension}");
            AnsiConsole.MarkupLine("[bold yellow]Saving API specifications...[/]");
            await File.WriteAllTextAsync(specFile, specs);
            config.ApiSpecPath = Path.GetFileName(specFile);
            config.ApiSpecUrl = null;
        }

        if (config.MetadataFile != null)
        {
            AnsiConsole.MarkupLine("[bold yellow]Copying metadata file...[/]");
            var fileName = config.MetadataFile;
            var name = Path.GetFileName(fileName);
            var metadataFile = Path.Combine(outputPath, name);
            config.MetadataFile = name;
            File.Copy(fileName, metadataFile, true);
        }

       
        var installBatchFile = Path.Combine(outputPath, $"setup.bat");
        await File.WriteAllBytesAsync(installBatchFile,QuickMCP.CLI.Resources.setup);

        installBatchFile = Path.Combine(outputPath, $"setup-linux.sh");
        await File.WriteAllBytesAsync(installBatchFile,QuickMCP.CLI.Resources.setup_linux);
        
        installBatchFile = Path.Combine(outputPath, $"setup-mac-os.sh");
        await File.WriteAllBytesAsync(installBatchFile,QuickMCP.CLI.Resources.setup_mac_os);

        AnsiConsole.MarkupLine("[bold yellow]Writing configuration to file...[/]");
        await File.WriteAllTextAsync(configFile,
            JsonSerializer.Serialize(config, jsonOption.GetTypeInfo(typeof(BuilderConfig))));

        AnsiConsole.MarkupLine($"[bold green]Configuration successfully saved to {configFile}[/]");
    }

    private async Task ConfigureAuth(BuildConfigCommandSettings settings, BuilderConfig config)
    {
        var auths = AuthenticatorFactory.GetAvailableAuthenticators().FirstOrDefault(s => s.Type == settings.AuthType);
        if (auths != null)
        {
            config.Authentication = new AuthConfig()
            {
                Type = auths.Type,
                Settings = auths.ConfigKeys.ToDictionary(k => k.Key, string? (k) => k.Description)
            };
        }

        if (settings.SkipAuthConfig != true && auths != null)
        {
            if (config.Authentication != null && config.Authentication.Settings != null)
            {
                AnsiConsole.MarkupLine("[bold yellow]Configure authentication (type `skip` or `cancel` to skip)...[/]");
                foreach (var auth in config.Authentication.Settings)
                {
                    var meta = auths.ConfigKeys.FirstOrDefault(s => s.Key == auth.Key);

                    AnsiConsole.MarkupLine($"[bold yellow]Configure {auth.Key}...[/]");
                    AnsiConsole.MarkupLine(
                        $"[bold Aquamarine1]{(meta.IsRequired ? "Required" : "Optional")} - {meta.Description}...[/]");

                    string? value;
                    do
                    {
                        var prompt = new TextPrompt<string>($"[bold cyan]Enter value for {auth.Key}[/]");
                        if (meta.IsRequired == false)
                        {
                            prompt.AllowEmpty();
                        }

                        value = await AnsiConsole.PromptAsync(prompt);
                    } while (meta.IsRequired && string.IsNullOrWhiteSpace(value));

                    if (value is "cancel" or "skip")
                        break;
                    config.Authentication.Settings[auth.Key] = (string.IsNullOrEmpty(value) ? null : value);
                }
            }
        }
    }

    private async Task<string> BuildAndSaveMetadata(McpServerInfo mcpServerInfo, BuildConfigCommandSettings settings, BuilderConfig config,
        string outputFolder, string prefix)
    {
       
        var client = new GenerativeModel(settings.AiApiKey, "gemini-2.0-flash-lite");

        DefaultSerializerOptions.CustomJsonTypeResolvers.Add(QuickMcpJsonSerializerContext.Default);
        var tools = mcpServerInfo.Tools.Values.ToList();

        List<UpdatedToolMetadata> metadataUpdated = new();

        AnsiConsole.MarkupLine($"[bold yellow]Generating metadata for {tools.Count} tools...[/]");
        await AnsiConsole.Progress()
            .StartAsync(async (ctx) =>
            {
                var task = ctx.AddTask("[bold blue]Processing tools...[/]", new ProgressTaskSettings
                {
                    MaxValue = mcpServerInfo.Tools.Count
                });

                for (int i = 0; i < mcpServerInfo.Tools.Count; i += 10)
                {
                    task.Description =
                        $"[bold blue]Processing tools {i + 1} to {Math.Min(i + 10, mcpServerInfo.Tools.Count)}[/]";

                    //AnsiConsole.MarkupLine($"[bold blue]Processing tools {i + 1} to {Math.Min(i + 10, builder.Tools.Count)}...[/]");
                    var toUpdate = tools.Skip(i).Take(10).ToList();

                    var information =
                        JsonSerializer.Serialize(toUpdate, QuickMcpJsonSerializerContext.Default.ListToolInfo);

                    var prompt =
                        $"Analyze the provided JSON API method definitions. For each method:\n\n1. Extract method name, parameters, and body schema\n2. Write clear descriptions for each element (method, parameters, body properties)";
                    prompt += "\n\n" + information;

                    var jsonResponse =
                        "[{\"old_method_name\":\"old method name\",\"updated_method_name\":\"updated method name compatible with open API specs\",\"updated_method_description\":\"updated method description\" \"parameters\":[{\"name\":\"parameter name\",\"description\":\"parameter description\"}], \"body_properties\":[{\"name\":\"body property name\",\"description\":\"body property description\"}],\"agentic_prompt_template\":\"prompt template to invoke the method in agentic work flow with parameters and body properties placeholder in curly brackets\"}]";
                    prompt += "\n\nOnly Reply in json format\r\n:" + jsonResponse;
                    int tried = 0;
                    GenerateContentResponse res = null;
                    do
                    {
                        //AnsiConsole.MarkupLine($"[italic yellow]Attempt {tried + 1} to generate metadata...[/]");
                        res = await client.GenerateContentAsync(prompt);

                        if (res != null)
                        {
                            break;
                        }

                        tried++;
                    } while (tried < 3);


                    if (res == null)
                    {
                        AnsiConsole.MarkupLine("[bold red]Unable to generate metadata after 3 attempts[/]");
                        throw new Exception("Unable to generate metadata");
                    }

                    var blocks = MarkdownExtractor.ExtractJsonBlocks(res.Text()).FirstOrDefault();
                    if (blocks == null)
                    {
                        AnsiConsole.MarkupLine("[bold red]Unable to generate metadata after 3 attempts[/]");
                        throw new Exception("Unable to generate metadata");
                    }

                    var jobj = JsonNode.Parse(blocks.Json);

                    foreach (var j in jobj.AsArray())
                    {
                        var data = new UpdatedToolMetadata();
                        data.Name = j["old_method_name"]?.GetValue<string>();
                        data.NewName = j["updated_method_name"]?.GetValue<string>();
                        data.Description = j["updated_method_description"]?.GetValue<string>();
                        data.Prompt = j["agentic_prompt_template"]?.GetValue<string>();
                        if (data.Parameters == null)
                            data.Parameters = new List<UpdatedParameterMetadata>();
                        foreach (var p in j["body_properties"]?.AsArray())
                        {
                            data.Parameters.Add(new UpdatedParameterMetadata()
                            {
                                Name = p["name"]?.GetValue<string>(),
                                Description = p["description"]?.GetValue<string>()
                            });
                        }

                        foreach (var p in j["parameters"]?.AsArray())
                        {
                            data.Parameters.Add(new UpdatedParameterMetadata()
                            {
                                Name = p["name"]?.GetValue<string>(),
                                Description = p["description"]?.GetValue<string>()
                            });
                        }

                        metadataUpdated.Add(data);
                    }

                    //metadataUpdated.AddRange(response);
                    task.Increment(10);
                }
            });

        var metadataFile = Path.Combine(Path.GetTempPath(), $"{prefix}_metadata.json");
        var updateMetadataConfig = new MetadataUpdateConfig()
        {
            Tools = metadataUpdated
        };

        await File.WriteAllTextAsync(metadataFile,
            JsonSerializer.Serialize(updateMetadataConfig, QuickMcpJsonSerializerContext.Default.MetadataUpdateConfig));
        AnsiConsole.MarkupLine($"[bold green]{metadataUpdated.Count} Metadata generated successfully![/]");
        AnsiConsole.MarkupLine($"[bold yellow]Metadata saved to {metadataFile}[/]");
        return metadataFile;
    }
}