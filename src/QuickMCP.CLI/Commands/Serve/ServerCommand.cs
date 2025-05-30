using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using QuickMCP.Abstractions;
using QuickMCP.Builders;
using QuickMCP.Extensions;
using QuickMCP.Types;
using Spectre.Console;
using Spectre.Console.Cli;
using Vertical.SpectreLogger;

namespace QuickMCP.CLI.Commands.Serve;


[Description("Starts the MCP server with the provided configuration or arguments.")]
public class ServerCommand : AsyncCommand<ServerCommandSettings>
{

    public override async Task<int> ExecuteAsync(CommandContext context, ServerCommandSettings settings)
    {
        var configFile = settings.ConfigPath;
        var configEnvVar = settings.ConfigEnvVar;
        if (!string.IsNullOrWhiteSpace(configEnvVar))
        {
            configFile = Environment.GetEnvironmentVariable(configEnvVar);
        }

        if (!string.IsNullOrWhiteSpace(settings.ServerId))
        {
            var localFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".quickmcp");
            var serverFolder = Path.Combine(localFolder, "servers", settings.ServerId);
            configFile = Path.Combine(serverFolder, "config.json");

            if (!File.Exists(configFile))
            {
                throw new FileNotFoundException($"Server configuration file not found for {settings.ServerId} at `{configFile}`");
            }
        }

        if (settings.EnableLogging == true)
        {
            AnsiConsole.MarkupLine($"[bold]Building MCP server info with configuration file:[/] {configFile}");
        }

        IMcpServerInfoBuilder? infoBuilder = null;

        if (!string.IsNullOrWhiteSpace(configFile))
        {
            infoBuilder = McpServerInfoBuilder.FromConfig(configFile);
        }
        else
        {
            var config = settings.GetBuilderConfig();
            infoBuilder = McpServerInfoBuilder.FromConfig(config);
        }

        if (settings.EnableLogging == true)
        {
            var loggerFactory = LoggerFactory.Create(s => s.AddSpectreConsole());
            infoBuilder.AddLogging(loggerFactory);
        }

        var mcpServerInfo = await infoBuilder.BuildAsync();

        if (settings.EnableLogging == true)
        {
            AnsiConsole.MarkupLine($"[bold]Initializing MCP server...[/]");
        }

        var logLevel = settings.EnableLogging == true ? LogLevel.Debug : LogLevel.None;

        var protocol = mcpServerInfo.BuilderConfig.HostProtocol?.ToLower();
        if (protocol == "http")
        {
            await RunHttpServerAsync(mcpServerInfo, configFile, logLevel, settings.EnableLogging == true);
        }
        else
        {
            await RunStdioServerAsync(mcpServerInfo, configFile, logLevel, settings.EnableLogging == true);
        }

        return 0;
    }

    [McpServerResourceType]
    private class ResourceRegistry
    {
        public static IReadOnlyDictionary<string, ResourceInfo>? ApiResources
        {
            get;
            set;
        }

        public static IReadOnlyDictionary<string, Resource>? ExternalResources
        {
            get;
            set;
        }

        public static string? Root { get; internal set; }

        [McpServerResource(UriTemplate = "resources/api/{uri}", Name = "Get API Resource Contents")]
        [Description("Gets API Resource Contents by Uri")]
        public static ResourceContents GetApiResourceContents(string uri)
        {
            var apiResource = ApiResources?.GetValueOrDefault(uri);
            if (apiResource == null)
            {
                return new TextResourceContents
                {
                    Uri = uri,
                    MimeType = "text/plain",
                    Text = $"API Resource '{uri}' not found."
                };
            }

            var blobData = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                { "schema", apiResource.Schema },
                { "metadata", apiResource.Metadata },
            });

            var base64Content = Convert.ToBase64String(blobData);
            return new BlobResourceContents
            {
                Uri = $"resources/api/{uri}",
                MimeType = "application/json+base64",
                Blob = base64Content
            };
        }

        [McpServerResource(UriTemplate = "resources/external/{uri}", Name = "Get External Resource Contents")]
        [Description("Gets External Resource Contents by Uri")]
        public static ResourceContents GetExternalResourceContents(string uri)
        {
            var externalResource = ExternalResources?.FirstOrDefault(r => r.Value.Uri.Equals(uri, StringComparison.OrdinalIgnoreCase)).Value;
            if (externalResource == null)
            {
                return new TextResourceContents
                {
                    Uri = uri,
                    MimeType = "text/plain",
                    Text = $"External Resource '{uri}' not found."
                };
            }
            return new BlobResourceContents
            {
                Uri = externalResource.Uri,
                MimeType = externalResource.MimeType ?? "application/octet-stream",
                Blob = ReadBase64Content($"resources/{externalResource.Uri}")
            };
        }

        public static ResourceContents GetResourceContents(string uri)
        {
            if (ApiResources?.ContainsKey(uri) == true)
            {
                return GetApiResourceContents(uri);
            }
            return GetExternalResourceContents(uri);
        }

        private static string ReadBase64Content(string fileName)
        {
            var fullPath = Root != null ? Path.Combine(Root, fileName) : fileName;
            if (!File.Exists(fullPath))
            {
                return Convert.ToBase64String(Encoding.UTF8.GetBytes($"File '{fullPath}' not found."));
            }

            var fileBytes = File.ReadAllBytes(fullPath);
            return Convert.ToBase64String(fileBytes);
        }

        internal static async ValueTask<ListResourcesResult> GetList(RequestContext<ListResourcesRequestParams> context, CancellationToken token)
        {
            var allResources_ = BuildResources();
            return new ListResourcesResult
            {
                Resources = allResources_ ?? new List<Resource>()
            };
        }

        private static List<Resource> BuildResources()
        {
            var allResources_ = new List<Resource>();
            if (ApiResources != null)
            {
                foreach (var kvp in ApiResources)
                {
                    allResources_.Add(new Resource
                    {
                        Name = kvp.Key,
                        Uri = $"resources/api/{kvp.Key}",
                        Description = kvp.Value.Metadata.GetValueOrDefault("description")?.ToString() ?? "No description available."
                    });
                }
            }
            if (ExternalResources != null)
            {
                foreach (var kvp in ExternalResources)
                {
                    var resource = kvp.Value;
                    allResources_.Add(new Resource
                    {
                        Name = resource.Name,
                        Uri = $"resources/external/{resource.Uri}",
                        Description = resource.Description ?? "No description available."
                    });
                }
            }
            return allResources_;
        }
    }

    public  async Task RunHttpServerAsync(McpServerInfo mcpServerInfo,
                                            string? configFile,
                                            LogLevel logLevel,
                                            bool enableLogging)
    {
        var hostBuilder = WebApplication.CreateBuilder();
        var mcpBuilder = hostBuilder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithQuickMCP(mcpServerInfo);

        var haveResources = false;
        if (mcpServerInfo.Resources.Count > 0)
        {
            ResourceRegistry.ApiResources = mcpServerInfo.Resources;
            mcpBuilder = mcpBuilder.WithResources<ResourceRegistry>()
                                    .WithListResourcesHandler(ResourceRegistry.GetList);
            haveResources = true;
        }

        if (mcpServerInfo.BuilderConfig.ExternalResources?.Count > 0)
        {
            ResourceRegistry.Root = Path.GetDirectoryName(configFile);
            ResourceRegistry.ExternalResources = mcpServerInfo.BuilderConfig.ExternalResources?.ToDictionary(r => r.Name, r => r) ?? new Dictionary<string, Resource>();
            haveResources = true;
        }

        if (haveResources)
        {
            mcpBuilder = mcpBuilder.WithResources<ResourceRegistry>()
                                    .WithListResourcesHandler(ResourceRegistry.GetList);
        }

        hostBuilder.Logging.SetMinimumLevel(logLevel).AddSpectreConsole(config =>
        {
            config.SetMinimumLevel(logLevel);
        });

        var app = hostBuilder.Build();
        app.MapMcp();

        if (enableLogging)
        {
            AnsiConsole.MarkupLine($"[bold]MCP server started...[/]");
        }
        await app.RunAsync();
    }

    public  async Task RunStdioServerAsync(McpServerInfo mcpServerInfo,
                                            string? configFile,
                                            LogLevel logLevel,
                                            bool enableLogging)
    {
        if (enableLogging)
        {
            AnsiConsole.MarkupLine($"[bold]Using STDIO transport...[/]");
        }
        var hostBuilder = Host.CreateApplicationBuilder();
        var mcpBuilder = hostBuilder.Services
            .AddMcpServer()
            .WithQuickMCP(mcpServerInfo)
            .WithStdioServerTransport();

        var haveResources = false;
        if (mcpServerInfo.Resources.Count > 0)
        {
            ResourceRegistry.ApiResources = mcpServerInfo.Resources;
            mcpBuilder = mcpBuilder.WithResources<ResourceRegistry>()
                                    .WithListResourcesHandler(ResourceRegistry.GetList);
            haveResources = true;
        }

        if (mcpServerInfo.BuilderConfig.ExternalResources?.Count > 0)
        {
            ResourceRegistry.Root = Path.GetDirectoryName(configFile);
            ResourceRegistry.ExternalResources = mcpServerInfo.BuilderConfig.ExternalResources?.ToDictionary(r => r.Name, r => r) ?? new Dictionary<string, Resource>();
            haveResources = true;
        }

        if (haveResources)
        {
            mcpBuilder = mcpBuilder.WithResources<ResourceRegistry>()
                                    .WithListResourcesHandler(ResourceRegistry.GetList);
        }

        hostBuilder.Logging.SetMinimumLevel(logLevel).AddSpectreConsole(config =>
        {
            config.SetMinimumLevel(logLevel);
        });

        var app = hostBuilder.Build();
        if (enableLogging)
        {
            AnsiConsole.MarkupLine($"[bold]MCP server started...[/]");
        }
        await app.RunAsync();
    }

}