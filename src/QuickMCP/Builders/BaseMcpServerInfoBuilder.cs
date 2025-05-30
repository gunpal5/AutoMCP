using System.Text.Json;
using System.Text.Json.Nodes;
using QuickMCP.Abstractions;
using QuickMCP.Helpers;
using QuickMCP.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCP = ModelContextProtocol.Protocol;

namespace QuickMCP.Builders;

/// <summary>
/// Base class for MCP tool builders that provides common functionality
/// </summary>
public abstract class BaseMcpServerInfoBuilder : IMcpServerInfoBuilder
{
    #region Fields and properties

    protected ILogger? Logger;
    protected ILoggerFactory LoggerFactory;
    protected readonly HttpClient HttpClient;
    protected readonly Dictionary<string, Prompt> Prompts;
    protected readonly Dictionary<string, MCP.Resource> ExternalResources;

    protected IAuthenticator? Authenticator;
    protected Func<string, bool>? PathExclusionFunc;
    protected Func<string, bool>? PathInclusionFunc;

    protected MetadataUpdateConfig? MetadataUpdateConfig;

    protected BuilderConfig _config = new BuilderConfig();

    protected List<string> ExcludedOperationIds
    {
        get
        {
            if (_config.ExcludedPaths == null)
                _config.ExcludedPaths = new List<string>();
            return _config.ExcludedPaths;
        }
        set => _config.ExcludedPaths = value;
    }

    protected string ServerName
    {
        get => _config.ServerName ?? string.Empty;
        set => _config.ServerName = value;
    }

    protected string ServerDescription
    {
        get => _config.ServerDescription ?? string.Empty;
        set => _config.ServerDescription = value;
    }

    protected string? OpenApiUrl
    {
        get => _config.ApiSpecUrl;
        set => _config.ApiSpecUrl = value;
    }

    protected string? OpenApiFilePath
    {
        get => _config.ApiSpecPath;
        set => _config.ApiSpecPath = value;
    }

    protected string? ConfigFilePath;

    protected List<string> IncludedOperationIds
    {
        get
        {
            if (_config.IncludedPaths == null)
                _config.IncludedPaths = new List<string>();
            return _config.IncludedPaths;
        }
        set => _config.IncludedPaths = value;
    }

    protected bool GenerateResourcesFlag
    {
        get => _config.GenerateResources;
        set => _config.GenerateResources = value;
    }

    protected bool GeneratePromptsFlag
    {
        get => _config.GeneratePrompts;
        set => _config.GeneratePrompts = value;
    }

    protected Dictionary<string, string> DefaultHeaders
    {
        get => _config.ServerHeaders ?? new Dictionary<string, string>();
        set => _config.ServerHeaders = value;
    }

    protected Dictionary<string, string> DefaultPathParams
    {
        get => _config.DefaultPathParameters ?? new Dictionary<string, string>();
        set => _config.DefaultPathParameters = value;
    }

    protected TimeSpan _timeout = TimeSpan.FromSeconds(30);
    protected HttpClient _httpClient;

    protected string BaseUrl
    {
        get => _config.ApiBaseUrl ?? string.Empty;
        set => _config.ApiBaseUrl = value;
    }

    protected readonly Dictionary<string, ToolInfo> RegisteredTools;
    protected readonly Dictionary<string, ResourceInfo> RegisteredResources;

    #endregion

    static BaseMcpServerInfoBuilder()
    {
        //OpenApiReaderRegistry.RegisterReader("yaml", new OpenApiYamlReader());
    }

    /// <summary>
    /// Creates a new instance of BaseMCPToolBuilder
    /// </summary>
    /// <param name="serverName">Name of the server</param>
    /// <param name="loggerFactory">Optional logger factory</param>
    protected BaseMcpServerInfoBuilder(string serverName)
    {
        ServerName = serverName;
        //ServerDescription = $"MCP Tools for {serverName}";
        HttpClient = new HttpClient();
        Prompts = new Dictionary<string, Prompt>();
        RegisteredTools = new Dictionary<string, ToolInfo>();
        RegisteredResources = new Dictionary<string, ResourceInfo>();
        ExternalResources = new Dictionary<string, MCP.Resource>();
    }

    /// <inheritdoc />
    public virtual IMcpServerInfoBuilder WithConfig(BuilderConfig config)
    {
        this._config = config;

        return this;
    }

    /// <inheritdoc />
    public abstract IMcpServerInfoBuilder FromUrl(string url);

    /// <inheritdoc />
    public abstract IMcpServerInfoBuilder FromFile(string filePath);

    /// <inheritdoc />
    public virtual IMcpServerInfoBuilder FromConfiguration(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Configuration file not found", configPath);
        }

        Logger?.LogInformation("Set configuration file path: {ConfigPath}", configPath);
        // Load configuration will happen in BuildAsync
        return this;
    }

    /// <inheritdoc />
    public virtual IMcpServerInfoBuilder ExcludePaths(Func<string, bool> exclusionPredicate)
    {
        PathExclusionFunc = exclusionPredicate;
        Logger?.LogInformation("Set path exclusion function");
        return this;
    }

    /// <inheritdoc />
    public virtual IMcpServerInfoBuilder ExcludePaths(IEnumerable<string> paths)
    {
        ExcludedOperationIds.AddRange(paths);
        Logger?.LogInformation("Excluded {Count} operation IDs", ExcludedOperationIds.Count);
        return this;
    }

    /// <inheritdoc />
    public virtual IMcpServerInfoBuilder OnlyForPaths(Func<string, bool> inclusionPredicate)
    {
        PathInclusionFunc = inclusionPredicate;
        Logger?.LogInformation("Set path inclusion function");
        return this;
    }

    /// <inheritdoc />
    public virtual IMcpServerInfoBuilder OnlyForPaths(IEnumerable<string> operationIds)
    {
        IncludedOperationIds.AddRange(operationIds);
        Logger?.LogInformation("Including only {Count} operation IDs", IncludedOperationIds.Count);
        return this;
    }

    /// <inheritdoc />
    public IMcpServerInfoBuilder SetBaseUrl(string baseUrl)
    {
        this.BaseUrl = baseUrl;
        return this;
    }

    /// <inheritdoc />
    public IMcpServerInfoBuilder WithDefaultPathParams(Dictionary<string, string> defaultPathParams)
    {
        foreach (var param in defaultPathParams)
        {
            this.DefaultPathParams[param.Key] = param.Value;
        }

        return this;
    }

    /// <inheritdoc />
    public IMcpServerInfoBuilder WithHttpClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        return this;
    }

    /// <inheritdoc />
    public IMcpServerInfoBuilder AddDefaultHeader(string key, string value)
    {
        DefaultHeaders[key] = value;
        return this;
    }

    /// <inheritdoc />
    public IMcpServerInfoBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }
    /// <inheritdoc />
    public IMcpServerInfoBuilder WithBaseUrl(string baseUrl)
    {
        this.BaseUrl = baseUrl;
        return this;
    }

    /// <inheritdoc />
    public virtual IMcpServerInfoBuilder AddAuthentication(IAuthenticator authenticator)
    {
        Authenticator = authenticator;
        Logger?.LogInformation("Added authenticator of type {AuthenticatorType}", authenticator.GetType().Name);
        return this;
    }

    /// <inheritdoc />
    public IMcpServerInfoBuilder AddLogging(ILoggerFactory loggerFactory)
    {
        this.LoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

        this.Logger = loggerFactory.CreateLogger(GetType());
        return this;
    }

    /// <inheritdoc />
    public virtual IMcpServerInfoBuilder GenerateResources(bool enabled = true)
    {
        GenerateResourcesFlag = enabled;
        Logger?.LogInformation("Resource generation {Status}", enabled ? "enabled" : "disabled");
        return this;
    }

    /// <inheritdoc />
    public virtual IMcpServerInfoBuilder GeneratePrompts(bool enabled = true)
    {
        GeneratePromptsFlag = enabled;
        Logger?.LogInformation("Prompt generation {Status}", enabled ? "enabled" : "disabled");
        return this;
    }

    /// <inheritdoc />
    public abstract Task<McpServerInfo> BuildAsync();

    /// <summary>
    /// Filters a collection of operations based on the configured filters
    /// </summary>
    /// <typeparam name="T">The type of operations</typeparam>
    /// <param name="operations">Dictionary of operations to filter</param>
    /// <returns>The filtered dictionary</returns>
    protected virtual Dictionary<string, OperationInfo> FilterOperations(Dictionary<string, OperationInfo> operations)
    {
        var result = new Dictionary<string, OperationInfo>(operations);

        // Apply exclusion predicate
        if (PathExclusionFunc != null)
        {
            var keysToRemove = new List<string>();
            foreach (var key in result.Keys)
            {
                if (PathExclusionFunc(result[key].Path))
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                result.Remove(key);
            }
        }

        // Apply inclusion predicate
        if (PathInclusionFunc != null)
        {
            var keysToKeep = new List<string>();
            foreach (var key in result.Keys)
            {
                if (PathInclusionFunc(result[key].Path))
                {
                    keysToKeep.Add(key);
                }
            }

            var keysToRemove = new List<string>();
            foreach (var key in result.Keys)
            {
                if (!keysToKeep.Contains(key))
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                result.Remove(key);
            }
        }

        // Apply excluded operation IDs
        if (ExcludedOperationIds != null && ExcludedOperationIds.Count > 0)
        {
            foreach (var opId in result.Keys)
            {
                var value = result[opId];
                if (ExcludedOperationIds.Any(s => value.Path.ToLower().Contains(s.ToLower())))
                    result.Remove(opId);
            }
        }

        // Apply included operation IDs
        if (IncludedOperationIds != null && IncludedOperationIds.Count > 0)
        {
            var keysToRemove = new List<string>();
            foreach (var key in result.Keys)
            {
                var value = result[key];
                if (value is OperationInfo opInfo)
                {
                    if (!IncludedOperationIds.Any(s => opInfo.Path.ToLower().Contains(s.ToLower())))
                    {
                        keysToRemove.Add(key);
                    }
                }
            }

            foreach (var key in keysToRemove)
            {
                result.Remove(key);
            }
        }

        return result;
    }

    /// <summary>
    /// Adds a tool to the collection of registered tools
    /// </summary>
    /// <param name="name">Name of the tool</param>
    /// <param name="function">Function that implements the tool</param>
    /// <param name="description">Description of the tool</param>
    /// <param name="metadata">Additional metadata for the tool</param>
    protected virtual void AddTool(
        string name,
        ToolInfo function,
        string description,
        ToolMetadata? metadata = null)
    {
        // Add server name as prefix
        var prefixedName = $"{ServerName}_{name}";
        var safeName = StringHelpers.SanitizeToolName(prefixedName);

        // Enhance description with server context
        var enhancedDescription = $"[{ServerName}] {description}";

        ToolMetadata finalMetadata;
        if (metadata == null)
        {
            finalMetadata = new ToolMetadata()
            {
                Name = safeName,
                Description = enhancedDescription,
                Tags = new List<string> { "api", ServerName },
                ServerInfo = new ServerInfo() { Name = ServerName, Description = ServerDescription }
            };
        }
        else
        {
            finalMetadata = metadata;
            finalMetadata.Name = safeName;
            finalMetadata.Description = enhancedDescription;


            // Add tags if not present
            if (finalMetadata.Tags.Count == 0)
            {
                finalMetadata.Tags = new List<string> { "api", ServerName };
            }

            // Add server info
            finalMetadata.ServerInfo = new ServerInfo() { Name = ServerName, Description = ServerDescription };
        }

        function.Name = metadata.Name ?? safeName;
        function.Metadata = finalMetadata;

        UpdateMetadata(function);
        RegisteredTools[safeName] = function;
    }

    protected void UpdateMetadata(ToolInfo function)
    {
        if (this.MetadataUpdateConfig != null)
        {
            var tool = this.MetadataUpdateConfig.Tools.FirstOrDefault(s => s.Name == function.Name);
            if (tool != null)
            {
                function.Name = tool.NewName ?? function.Name;
                function.Name = StringHelpers.SanitizeToolName(function.Name);
                function.Metadata.Description = tool.Description ?? function.Metadata.Description;
                function.Metadata.Tags = tool.Tags ?? function.Metadata.Tags;
                function.Metadata.Name = tool.Name ?? function.Metadata.Name;
                RecursivelyUpdateSchema(function.Metadata.InputSchema, tool);
            }
        }
    }

    private void RecursivelyUpdateSchema(JsonNode? metadataInputSchema, UpdatedToolMetadata toolMetadata)
    {
        if (metadataInputSchema == null)
            return;

        foreach (var param in toolMetadata.Parameters)
        {
            if (string.IsNullOrEmpty(param.Description))
                continue;
            var val = metadataInputSchema["properties"]?[param.Name];
            if (val != null)
            {
                val["description"] = param.Description;
            }

            val = metadataInputSchema["properties"]?["body"]?["properties"]?[param.Name];
            if (val != null)
            {
                val["description"] = param.Description;
            }
        }
    }

    /// <summary>
    /// Creates a standard tool function for making API requests
    /// </summary>
    /// <param name="method">HTTP method (GET, POST, etc.)</param>
    /// <param name="url">URL template for the request</param>
    /// <param name="parameters">List of parameters for the request</param>
    /// <returns>Function that handles the tool request</returns>
    protected virtual ToolInfo CreateToolFunction(
        string method, string url, List<Parameter> parameters)
    {
        string contentType = "application/json";
        if (method == "POST")
        {
            var param = parameters.FirstOrDefault(s => s.In == "body");
            if (param != null)
            {
                contentType = param.ContentType ?? "application/json";
            }
        }

        var toolInfo = new ToolInfo()
        {
            Method = method,
            Url = url,
            MimeType = contentType,
            Parameters = parameters
        };
        return toolInfo;
    }

    /// <summary>
    /// Loads configuration from a JSON file
    /// </summary>
    /// <param name="configPath">Path to the configuration file</param>
    /// <returns>The deserialized configuration object</returns>
    protected virtual async Task<T?> LoadConfigurationAsync<T>(string configPath) where T : class
    {
        try
        {
            Logger?.LogInformation("Loading configuration from: {ConfigPath}", configPath);

#if NETSTANDARD2_0_OR_GREATER
            var configJson = File.ReadAllText(configPath);
#else
            var configJson = await File.ReadAllTextAsync(configPath);
#endif
            var config = (T?)JsonSerializer.Deserialize(configJson, typeof(T), QuickMcpJsonSerializerContext.Default);

            if (config == null)
            {
                Logger?.LogError("Invalid configuration file format");
            }

            return config;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load configuration from: {ConfigPath}", configPath);
            return null;
        }
    }

    /// <summary>
    /// Creates a MCPToolCollection from the current state
    /// </summary>
    /// <returns>A collection of the built tools, resources, and prompts</returns>
    protected virtual McpServerInfo CreateToolCollection()
    {
        return new McpServerInfo(
            ServerName,
            ServerDescription,
            RegisteredTools,
            GenerateResourcesFlag ? RegisteredResources : new Dictionary<string, ResourceInfo>(),
            GeneratePromptsFlag ? Prompts : new Dictionary<string, Prompt>(),
            this._config
        );
    }

    public static void AdjustPaths(BuilderConfig config, string? configFile = null)
    {
        config.ApiSpecPath =
            config.ApiSpecPath?.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        config.MetadataFile =
            config.MetadataFile?.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var additionalPaths = new List<string>();
        if (!string.IsNullOrEmpty(configFile))
        {
            additionalPaths.Add(Path.GetDirectoryName(configFile));
        }

        if (config.ApiSpecPath != null && !string.IsNullOrEmpty(config.ApiSpecPath))
        {
            config.ApiSpecPath = PathHelper.GetFullPath(config.ApiSpecPath, additionalPaths.ToArray());
        }

        if (config.MetadataFile != null && !string.IsNullOrEmpty(config.MetadataFile))
        {
            config.MetadataFile = PathHelper.GetFullPath(config.MetadataFile, additionalPaths.ToArray());
        }
    }
}