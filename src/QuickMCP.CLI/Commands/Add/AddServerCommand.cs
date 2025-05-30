using System.ComponentModel;
using System.Text.Json;
using QuickMCP.Abstractions;
using QuickMCP.Builders;
using QuickMCP.Helpers;
using QuickMCP.Types;
using Spectre.Console;
using Spectre.Console.Cli;

namespace QuickMCP.CLI.Commands.Add;

[Description("Add/Install a new server to directly use with commandline.")]
public class AddServerCommand:AsyncCommand<AddServerSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, AddServerSettings settings)
    {
        var configFile = settings.ConfigurationFile;

        AnsiConsole.MarkupLine($"[bold]Verifying MCP server configuration file:[/] {configFile}");

        IMcpServerInfoBuilder? infoBuilder = null;

        var info = await McpServerInfoBuilder.FromConfig(configFile).BuildAsync();
        if(info.Tools.Count ==0)
        {
            AnsiConsole.MarkupLine("[red]No tools found in configuration file.[/]");
            return 1;
        }
        var serverName = settings.ServerName ?? info.Name;
        var serverId = StringHelpers.SanitizeServerName(serverName);
        var localFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".quickmcp");
        var serverFolder = Path.Combine(localFolder, "servers", serverId);

        if (Directory.Exists(serverFolder))
        {
            if (await AnsiConsole.ConfirmAsync(
                    $"Server [bold]{serverName}[/] already exists. Do you want to overwrite it?"))
            {
                Directory.Delete(serverFolder, true);
            }
        }
        Directory.CreateDirectory(serverFolder);
        
        //Copy Configuration File
        File.Copy(configFile, Path.Combine(serverFolder, "config.json"));
        
        //Copy Metadata
        if (!string.IsNullOrEmpty(info.BuilderConfig.MetadataFile))
        {
            var filePath = PathHelper.GetFullPath(info.BuilderConfig.MetadataFile, [Path.GetDirectoryName(configFile) ?? ""]);
            var fileName = Path.GetFileName(info.BuilderConfig.MetadataFile);
            File.Copy(filePath, Path.Combine(serverFolder, fileName));
        }
        
        //Copy Spec
        if (!string.IsNullOrEmpty(info.BuilderConfig.ApiSpecPath))
        {
            var filePath = PathHelper.GetFullPath(info.BuilderConfig.ApiSpecPath, [Path.GetDirectoryName(configFile) ?? ""]);
            var fileName = Path.GetFileName(info.BuilderConfig.ApiSpecPath);
            File.Copy(filePath, Path.Combine(serverFolder, fileName));
        }
        //Copy Serve Info File
        
        var serverInfoFile = Path.Combine(serverFolder, "server_info.json");
        var serverInfo = new ServerConfiguration()
        {
            ServerName = serverName,
            Description = info.Description,
            ConfigurationFile = "config.json"
        };

        var serverInfoContent = JsonSerializer.Serialize(serverInfo, QuickMcpJsonSerializerContext.Default.ServerConfiguration);
        await File.WriteAllTextAsync(serverInfoFile, serverInfoContent);
        
        AnsiConsole.MarkupLine($"[green]Server `{serverId}` added successfully.[/]");
        return 0;
    }
}