using System.Text.Json;
using System.Text.Json.Nodes;
using QuickMCP.Abstractions;
using QuickMCP.Helpers;
using QuickMCP.Types;
using Microsoft.Extensions.Logging;

namespace QuickMCP.Builders;

/// <summary>
/// Implementation of IMcpServerBuilder for Google Discovery API specifications
/// </summary>
public class GoogleDiscoveryMcpServerInfoBuilder : HttpMcpServerInfoBuilder
{
    private string? _discoveryUrl;
    private string? _discoveryFilePath;
    private string? _configFilePath;
    private JsonDocument? _discoveryDocument;

    /// <summary>
    /// Creates a new instance of GoogleDiscoveryToolBuilder
    /// </summary>
    /// <param name="serverName">Name of the server</param>
    /// <param name="loggerFactory">Optional logger factory</param>
    public GoogleDiscoveryMcpServerInfoBuilder(string serverName = "google_api")
        : base(serverName)
    {
    }

    /// <inheritdoc />
    public override IMcpServerInfoBuilder FromUrl(string url)
    {
        _discoveryUrl = url;
        Logger?.LogInformation("Set Google Discovery URL: {Url}", url);
        return this;
    }

    /// <inheritdoc />
    public override IMcpServerInfoBuilder FromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Google Discovery specification file not found", filePath);
        }

        _discoveryFilePath = filePath;
        Logger?.LogInformation("Set Google Discovery file path: {FilePath}", filePath);
        return this;
    }

    /// <inheritdoc />
    public override IMcpServerInfoBuilder FromConfiguration(string configPath)
    {
        _configFilePath = configPath;
        return base.FromConfiguration(configPath);
    }

    /// <inheritdoc />
    public override async Task<McpServerInfo> BuildAsync()
    {
        try
        {
            // Load Discovery specification from the appropriate source
            if (!string.IsNullOrEmpty(_discoveryUrl))
            {
                _discoveryDocument = await LoadDiscoveryFromUrlAsync(_discoveryUrl);
            }
            else if (!string.IsNullOrEmpty(_discoveryFilePath))
            {
                _discoveryDocument = await LoadDiscoveryFromFileAsync(_discoveryFilePath);
            }
            else if (!string.IsNullOrEmpty(_configFilePath))
            {
                var config = await LoadConfigurationAsync<BuilderConfig>(_configFilePath);
                if (config != null)
                {
                    return await ProcessConfigurationAsync(config);
                }
            }

            if (_discoveryDocument == null)
            {
                Logger.LogWarning("No Google Discovery document was loaded. No tools will be registered.");
                return CreateToolCollection();
            }

            await ProcessDiscoveryDocumentAsync(_discoveryDocument);

            // Generate resources if enabled
            if (GenerateResourcesFlag)
            {
                RegisterResources(_discoveryDocument);
            }

            // Generate prompts if enabled
            if (GeneratePromptsFlag)
            {
                GeneratePrompts(_discoveryDocument);
            }

            return CreateToolCollection();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error building MCP tools from Google Discovery document");
            throw;
        }
    }

    private async Task<JsonDocument?> LoadDiscoveryFromUrlAsync(string url)
    {
        try
        {
            Logger?.LogInformation("Loading Google Discovery spec from URL: {Url}", url);

            var response = await HttpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync();
            var document = await JsonDocument.ParseAsync(stream);

            Logger?.LogInformation("Successfully loaded Google Discovery spec from URL");
            return document;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load Google Discovery spec from URL: {Url}", url);
            return null;
        }
    }

    private async Task<JsonDocument?> LoadDiscoveryFromFileAsync(string filePath)
    {
        try
        {
            Logger?.LogInformation("Loading Google Discovery spec from file: {FilePath}", filePath);

            #if NETSTANDARD2_0_OR_GREATER
            var fileContent = File.ReadAllText(filePath);
            #else
            var fileContent = await File.ReadAllTextAsync(filePath);
            #endif  
            var document = JsonDocument.Parse(fileContent);

            Logger?.LogInformation("Successfully loaded Google Discovery spec from file");
            return document;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load Google Discovery spec from file: {FilePath}", filePath);
            return null;
        }
    }

    private async Task<McpServerInfo> ProcessConfigurationAsync(BuilderConfig config)
    {
        try
        {
           
            InitializeFromConfig(config);
            // Process Discovery URL if provided
            if (!string.IsNullOrEmpty(config.ApiSpecUrl))
            {
                _discoveryDocument = await LoadDiscoveryFromUrlAsync(config.ApiSpecUrl);
            }
            else if (!string.IsNullOrEmpty(config.ApiSpecPath))
            {
                _discoveryDocument = await LoadDiscoveryFromFileAsync(config.ApiSpecPath);
            }
            else throw new ArgumentException("No API specification URL or path provided");
            
            if (_discoveryDocument != null)
            {
                await ProcessDiscoveryDocumentAsync(_discoveryDocument);

                // Generate resources if enabled
                if (GenerateResourcesFlag)
                {
                    RegisterResources(_discoveryDocument);
                }

                // Generate prompts if enabled
                if (GeneratePromptsFlag)
                {
                    GeneratePrompts(_discoveryDocument);
                }
            }
           

            Logger?.LogInformation("Successfully processed configuration");

            return CreateToolCollection();
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to process configuration");
            throw;
        }
    }

    private async Task ProcessDiscoveryDocumentAsync(JsonDocument discoveryDoc)
    {
        try
        {
            Logger?.LogInformation("Processing Google Discovery document");

            // Extract API info
            var root = discoveryDoc.RootElement;
            string apiName = root.GetProperty("name").GetString() ?? "GoogleAPI";
            string apiVersion = root.GetProperty("version").GetString() ?? "v1";
            string baseUrl = root.GetProperty("baseUrl").GetString() ?? "https://api.example.com";
            var description = root.TryGetProperty("description", out var desc) ? desc.GetString():null;
            
            // Update server description with API info
            
            ServerDescription = description ?? $"Google API: {apiName} ({apiVersion})";

            var schemas = root.GetProperty("schemas");
            // Extract resources and methods
            if (root.TryGetProperty("resources", out var resources))
            {
                var operationsInfo = ExtractOperationsInfo(resources, baseUrl, schemas);
                
                // Apply filters
                var filteredOps = FilterOperations(operationsInfo);

                Logger?.LogInformation("Registering {Count} tools after filtering", filteredOps.Count);

                foreach (var op in filteredOps)
                {
                    var opId = op.Key;
                    var info = op.Value;
                    try
                    {
                        // Generate tool metadata
                        var toolMeta = GenerateToolMetadata(opId, info);

                        // Create tool function
                        var toolFunc = CreateToolFunction(info.Method, info.Path, info.Parameters);

                        // Register tool
                        AddTool(opId, toolFunc, info.Summary, toolMeta);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Failed to register tool for operation {OperationId}", opId);
                    }
                }
            }
            else
            {
                Logger?.LogWarning("Google Discovery document has no resources");
            }

            Logger?.LogInformation("Successfully processed Google Discovery document");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to process Google Discovery document");
            throw;
        }
    }

    private Dictionary<string, OperationInfo> ExtractOperationsInfo(JsonElement resources, string baseUrl,JsonElement schemas)
    {
        var operationsInfo = new Dictionary<string, OperationInfo>();

        foreach (var resource in resources.EnumerateObject())
        {
            string resourceName = resource.Name;

            if (resource.Value.TryGetProperty("methods", out var methods))
            {
                // Process methods directly in this resource
                ProcessMethods(methods, resourceName, "", baseUrl, operationsInfo,schemas);
            }

            // Process sub-resources recursively
            if (resource.Value.TryGetProperty("resources", out var subResources))
            {
                foreach (var subResource in subResources.EnumerateObject())
                {
                    string subResourceName = subResource.Name;
                    string fullResourcePath = $"{resourceName}.{subResourceName}";

                    if (subResource.Value.TryGetProperty("methods", out var subMethods))
                    {
                        ProcessMethods(subMethods, resourceName, subResourceName, baseUrl, operationsInfo,schemas);
                    }
                }
            }
        }

        return operationsInfo;
    }

    private void ProcessMethods(
        JsonElement methods,
        string resourceName,
        string subResourceName,
        string baseUrl,
        Dictionary<string, OperationInfo> operationsInfo,
        JsonElement schemas)
    {
        foreach (var method in methods.EnumerateObject())
        {
            string methodName = method.Name;
            string opId = string.IsNullOrEmpty(subResourceName)
                ? $"{resourceName}.{methodName}"
                : $"{resourceName}.{subResourceName}.{methodName}";

            string httpMethod = method.Value.GetProperty("httpMethod").GetString() ?? "GET";
            string path = method.Value.GetProperty("path").GetString() ?? "";
            path = path.Contains("+") ? method.Value.GetProperty("flatPath").GetString()?? path : path;
            string description = method.Value.TryGetProperty("description", out var desc)
                ? desc.GetString() ?? opId
                : opId;

            var parameters = new List<Parameter>();

            // Process parameters
            if (method.Value.TryGetProperty("parameters", out var paramsElement))
            {
                foreach (var param in paramsElement.EnumerateObject())
                {
                    string paramName = param.Name;
                    bool required = param.Value.TryGetProperty("required", out var reqProp) && reqProp.GetBoolean();
                    string location = param.Value.TryGetProperty("location", out var locProp)
                        ? locProp.GetString() ?? "query"
                        : "query";
                    string paramDesc = param.Value.TryGetProperty("description", out var paramDescProp)
                        ? paramDescProp.GetString() ?? ""
                        : "";

                    // Convert Google parameter type to OpenAPI-compatible schema
                    var schema = new JsonObject();
                    if (param.Value.TryGetProperty("type", out var typeProp))
                    {
                        string type = typeProp.GetString() ?? "string";
                        schema["type"] = ConvertGoogleTypeToJsonSchema(type);
                    }
                    else
                    {
                        schema["type"] = "string";
                    }

                    // Add enum values if present
                    if (param.Value.TryGetProperty("enum", out var enumProp))
                    {
                        var enumArray = new JsonArray();
                        foreach (var enumVal in enumProp.EnumerateArray())
                        {
                            enumArray.Add(enumVal.GetString());
                        }

                        schema["enum"] = enumArray;
                    }

                    parameters.Add(new Parameter
                    {
                        Name = paramName,
                        In = location.ToLowerInvariant(),
                        Required = required,
                        Schema = schema,
                        Description = paramDesc
                    });
                }
            }

            // Process request body if present
            if (method.Value.TryGetProperty("request", out var requestProp))
            {
                var bodySchema = new JsonObject
                {
                    ["type"] = "object"
                };

                if (requestProp.TryGetProperty("$ref", out var refProp))
                {
                    string schemaRef = refProp.GetString() ?? "";
                    if (schemas.TryGetProperty(schemaRef, out var schemaX))
                    {
                        bodySchema = ConvertGoogleSchemaToResourceSchema(schemaX);
                    }
                }

                parameters.Add(new Parameter
                {
                    Name = "body",
                    In = "body",
                    Required = true,
                    Schema = bodySchema,
                    Description = "Request body"
                });
            }
            
            JsonNode? responseSchema = null;
            // Process request body if present
            if (method.Value.TryGetProperty("response", out var responseProp))
            {
                responseSchema = new JsonObject
                {
                    ["type"] = "object"
                };

                if (responseProp.TryGetProperty("$ref", out var refProp))
                {
                    string schemaRef = refProp.GetString() ?? "";
                    if (schemas.TryGetProperty(schemaRef, out var schemaX))
                    {
                        responseSchema = ConvertGoogleSchemaToResourceSchema(schemaX);
                    }
                }
            }

            // Create operation info
            operationsInfo[opId] = new OperationInfo
            {
                Summary = description,
                Parameters = parameters,
                Path = path,
                Method = httpMethod,
                ResponseSchema = responseSchema,
                Tags = new List<string> { resourceName }
            };
        }
    }

    private string ConvertGoogleTypeToJsonSchema(string googleType)
    {
        return googleType.ToLowerInvariant() switch
        {
            "string" => "string",
            "integer" => "integer",
            "number" => "number",
            "boolean" => "boolean",
            "object" => "object",
            "array" => "array",
            _ => "string" // Default to string for unknown types
        };
    }

    private ToolMetadata GenerateToolMetadata(string opId, OperationInfo info)
    {
        // Generate proper tool metadata
        var properties = new JsonObject();
        var required = new List<string>();

        foreach (var param in info.Parameters)
        {
            var name = param.Name;
            var pSchema = param.Schema;

            if (pSchema != null)
            {
                properties[name] = pSchema;
            }
            else
            {
                var pType = "string";
                properties[name] = new JsonObject
                {
                    ["type"] = pType,
                    ["description"] = param.Description ?? $"Type: {pType}"
                };
            }

            if (param.Required)
            {
                required.Add(name);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Count > 0)
        {
            var requiredArray = new JsonArray();
            foreach (var req in required)
            {
                requiredArray.Add(req);
            }

            schema["required"] = requiredArray;
        }

        // Add tags
        var tags = new List<string>(info.Tags);
        tags.Add(ServerName);
        tags.Add("google_api");

        var metadata = new ToolMetadata()
        {
            InputSchema = schema,
            Tags = tags
        };
      

        if (info.ResponseSchema != null)
        {
            metadata.ResponseSchema = info.ResponseSchema;
        }

        return metadata;
    }

    private void RegisterResources(JsonDocument discoveryDoc)
    {
        try
        {
            var root = discoveryDoc.RootElement;

            if (!root.TryGetProperty("schemas", out var schemas) || schemas.ValueKind != JsonValueKind.Object)
            {
                Logger?.LogInformation("No schemas found in Discovery document for resource generation");
                return;
            }

            Logger?.LogInformation("Registering {Count} resources from Google Discovery schemas",
                schemas.EnumerateObject().Count());

            foreach (var schema in schemas.EnumerateObject())
            {
                string schemaName = schema.Name;
                var prefixedName = $"{ServerName}_{schemaName}";
                var safeName = StringHelpers.SanitizeName(prefixedName);

                // Convert Google schema to MCPResource schema
                var resourceSchema = ConvertGoogleSchemaToResourceSchema(schema.Value);
                var resourceDescription = schema.Value.TryGetProperty("description", out var desc)
                    ? $"[{ServerName}] {desc.GetString()}"
                    : $"[{ServerName}] Resource for {schemaName}";

                RegisteredResources[safeName] = new ResourceInfo
                {
                    Schema = resourceSchema,
                    Metadata = new Dictionary<string, object>
                    {
                        ["name"] = safeName,
                        ["description"] = resourceDescription,
                        ["serverInfo"] = new Dictionary<string, string> { ["name"] = ServerName },
                        ["tags"] = new List<string> { "resource", ServerName, "google_api" }
                    }
                };
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to register resources from Google Discovery document");
        }
    }

    private JsonObject ConvertGoogleSchemaToResourceSchema(JsonElement schema)
    {
        var result = new JsonObject
        {
            ["type"] = "object"
        };

        // Add description if present
        if (schema.TryGetProperty("description", out var desc))
        {
            result["description"] = desc.GetString();
        }

        // Process properties
        if (schema.TryGetProperty("properties", out var props))
        {
            var properties = new JsonObject();

            foreach (var prop in props.EnumerateObject())
            {
                var propName = prop.Name;
                var propObj = new JsonObject();

                // Add type
                if (prop.Value.TryGetProperty("type", out var typeProp))
                {
                    propObj["type"] = ConvertGoogleTypeToJsonSchema(typeProp.GetString() ?? "string");
                }
                else
                {
                    propObj["type"] = "string";
                }

                // Add description
                if (prop.Value.TryGetProperty("description", out var propDesc))
                {
                    propObj["description"] = propDesc.GetString();
                }

                // Add enum values if present
                if (prop.Value.TryGetProperty("enum", out var enumProp))
                {
                    var enumArray = new JsonArray();
                    foreach (var enumVal in enumProp.EnumerateArray())
                    {
                        enumArray.Add(enumVal.GetString());
                    }

                    propObj["enum"] = enumArray;
                }

                // Handle nested objects
                if (prop.Value.TryGetProperty("properties", out var nestedProps))
                {
                    propObj["properties"] = ConvertGoogleSchemaToResourceSchema(prop.Value)["properties"];
                }

                // Handle arrays
                if (propObj["type"]?.GetValue<string>() == "array" &&
                    prop.Value.TryGetProperty("items", out var items))
                {
                    propObj["items"] = ConvertGoogleSchemaToResourceSchema(items);
                }

                properties[propName] = propObj;
            }

            result["properties"] = properties;
        }

        return result;
    }

    private void GeneratePrompts(JsonDocument discoveryDoc)
    {
        try
        {
            Logger?.LogInformation("Generating prompts from Google Discovery document");

            var root = discoveryDoc.RootElement;
            string apiName = root.GetProperty("name").GetString() ?? "GoogleAPI";
            string apiVersion = root.GetProperty("version").GetString() ?? "v1";
            string apiTitle = $"{apiName} {apiVersion}";

            // Generate general usage prompt
            var generalPrompt = GenerateGeneralUsagePrompt(discoveryDoc);
            Prompts[generalPrompt.Name] = generalPrompt;

            // Generate resource-specific prompts
            if (root.TryGetProperty("resources", out var resources))
            {
                var resourcePrompts = GenerateResourcePrompts(resources, apiTitle);
                foreach (var prompt in resourcePrompts)
                {
                    Prompts[prompt.Name] = prompt;
                }
            }

            Logger?.LogInformation("Generated {Count} prompts", Prompts.Count);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error generating prompts");
        }
    }

    private Prompt GenerateGeneralUsagePrompt(JsonDocument discoveryDoc)
    {
        var root = discoveryDoc.RootElement;
        string apiName = root.GetProperty("name").GetString() ?? "GoogleAPI";
        string apiVersion = root.GetProperty("version").GetString() ?? "v1";
        string apiTitle = $"{apiName} {apiVersion}";
        string apiDesc = root.TryGetProperty("description", out var desc) ? desc.GetString() ?? apiTitle : apiTitle;

        var generalPromptContent = new System.Text.StringBuilder();

        generalPromptContent.AppendLine($"# {ServerName} - Google API Guide for {apiTitle}");
        generalPromptContent.AppendLine();
        generalPromptContent.AppendLine(apiDesc);
        generalPromptContent.AppendLine();
        generalPromptContent.AppendLine($"## Available Methods:");
        generalPromptContent.AppendLine();

        if (root.TryGetProperty("resources", out var resources))
        {
            foreach (var resource in resources.EnumerateObject())
            {
                string resourceName = resource.Name;
                generalPromptContent.AppendLine($"### {resourceName}");

                if (resource.Value.TryGetProperty("methods", out var methods))
                {
                    foreach (var method in methods.EnumerateObject())
                    {
                        string methodName = method.Name;
                        string opId = $"{resourceName}.{methodName}";
                        string safeOpId = StringHelpers.SanitizeToolName($"{ServerName}_{opId}");

                        string httpMethod = method.Value.GetProperty("httpMethod").GetString() ?? "GET";
                        string path = method.Value.TryGetProperty("path", out var pathProp)
                            ? pathProp.GetString() ?? ""
                            : "";
                        string description = method.Value.TryGetProperty("description", out var methodDesc)
                            ? methodDesc.GetString() ?? ""
                            : "";

                        generalPromptContent.AppendLine($"#### {safeOpId}");
                        generalPromptContent.AppendLine($"- Path: `{path}` (HTTP {httpMethod})");
                        generalPromptContent.AppendLine($"- Description: {description}");

                        // Add parameter details
                        if (method.Value.TryGetProperty("parameters", out var parameters))
                        {
                            generalPromptContent.AppendLine("- Parameters:");
                            foreach (var param in parameters.EnumerateObject())
                            {
                                string paramName = param.Name;
                                bool required = param.Value.TryGetProperty("required", out var reqProp) &&
                                                reqProp.GetBoolean();
                                string paramType = param.Value.TryGetProperty("type", out var typeProp)
                                    ? typeProp.GetString() ?? "string"
                                    : "string";
                                string paramDesc = param.Value.TryGetProperty("description", out var paramDescProp)
                                    ? paramDescProp.GetString() ?? ""
                                    : "";

                                generalPromptContent.AppendLine(
                                    $"  - `{paramName}` ({paramType}): {paramDesc} [{(required ? "Required" : "Optional")}]");
                            }
                        }

                        generalPromptContent.AppendLine();
                    }
                }

                // Handle sub-resources
                if (resource.Value.TryGetProperty("resources", out var subResources))
                {
                    foreach (var subResource in subResources.EnumerateObject())
                    {
                        string subResourceName = subResource.Name;
                        generalPromptContent.AppendLine($"### {resourceName}.{subResourceName}");

                        if (subResource.Value.TryGetProperty("methods", out var subMethods))
                        {
                            foreach (var method in subMethods.EnumerateObject())
                            {
                                string methodName = method.Name;
                                string opId = $"{resourceName}.{subResourceName}.{methodName}";
                                string safeOpId = StringHelpers.SanitizeToolName($"{ServerName}_{opId}");

                                string httpMethod = method.Value.GetProperty("httpMethod").GetString() ?? "GET";
                                string path = method.Value.TryGetProperty("path", out var pathProp)
                                    ? pathProp.GetString() ?? ""
                                    : "";
                                string description = method.Value.TryGetProperty("description", out var methodDesc)
                                    ? methodDesc.GetString() ?? ""
                                    : "";

                                generalPromptContent.AppendLine($"#### {safeOpId}");
                                generalPromptContent.AppendLine($"- Path: `{path}` (HTTP {httpMethod})");
                                generalPromptContent.AppendLine($"- Description: {description}");

                                // Add parameter details
                                if (method.Value.TryGetProperty("parameters", out var parameters))
                                {
                                    generalPromptContent.AppendLine("- Parameters:");
                                    foreach (var param in parameters.EnumerateObject())
                                    {
                                        string paramName = param.Name;
                                        bool required = param.Value.TryGetProperty("required", out var reqProp) &&
                                                        reqProp.GetBoolean();
                                        string paramType = param.Value.TryGetProperty("type", out var typeProp)
                                            ? typeProp.GetString() ?? "string"
                                            : "string";
                                        string paramDesc =
                                            param.Value.TryGetProperty("description", out var paramDescProp)
                                                ? paramDescProp.GetString() ?? ""
                                                : "";

                                        generalPromptContent.AppendLine(
                                            $"  - `{paramName}` ({paramType}): {paramDesc} [{(required ? "Required" : "Optional")}]");
                                    }
                                }

                                generalPromptContent.AppendLine();
                            }
                        }
                    }
                }
            }
        }

        var promptName = $"{ServerName}_api_general_usage";
        var promptDescription = $"General guidance for using Google {apiTitle} API";

        return new Prompt(promptName, generalPromptContent.ToString(), promptDescription);
    }

    private List<Prompt> GenerateResourcePrompts(JsonElement resources, string apiTitle)
    {
        var prompts = new List<Prompt>();

        foreach (var resource in resources.EnumerateObject())
        {
            string resourceName = resource.Name;
            var examplePromptContent = new System.Text.StringBuilder();

            examplePromptContent.AppendLine($"# {ServerName} - Examples for working with {resourceName}");
            examplePromptContent.AppendLine();
            examplePromptContent.AppendLine($"Common scenarios for using {resourceName} in the {apiTitle} API:");
            examplePromptContent.AppendLine();

            if (resource.Value.TryGetProperty("methods", out var methods))
            {
                foreach (var method in methods.EnumerateObject())
                {
                    string methodName = method.Name;
                    string opId = $"{resourceName}.{methodName}";
                    string safeOpId = StringHelpers.SanitizeToolName($"{ServerName}_{opId}");

                    string description = method.Value.TryGetProperty("description", out var methodDesc)
                        ? methodDesc.GetString() ?? methodName
                        : methodName;

                    examplePromptContent.AppendLine($"## Using {methodName}");
                    examplePromptContent.AppendLine();
                    examplePromptContent.AppendLine(description);
                    examplePromptContent.AppendLine();
                    examplePromptContent.AppendLine("```");

                    // Create example tool call
                    examplePromptContent.Append($"{{{{tool.{safeOpId}(");

                    // Add required parameters
                    var requiredParams = new List<string>();
                    if (method.Value.TryGetProperty("parameters", out var parameters))
                    {
                        foreach (var param in parameters.EnumerateObject())
                        {
                            if (param.Value.TryGetProperty("required", out var reqProp) && reqProp.GetBoolean())
                            {
                                string paramName = param.Name;
                                string paramType = param.Value.TryGetProperty("type", out var typeProp)
                                    ? typeProp.GetString() ?? "string"
                                    : "string";

                                string exampleValue;
                                if (paramType == "string")
                                {
                                    exampleValue = $"\"{paramName}_example\"";
                                }
                                else if (paramType == "integer" || paramType == "number")
                                {
                                    exampleValue = "1";
                                }
                                else if (paramType == "boolean")
                                {
                                    exampleValue = "true";
                                }
                                else
                                {
                                    exampleValue = "\"example\"";
                                }

                                requiredParams.Add($"{paramName}={exampleValue}");
                            }
                        }
                    }

                    if (requiredParams.Count > 0)
                    {
                        examplePromptContent.AppendLine();
                        examplePromptContent.AppendLine("    " + string.Join(",\n    ", requiredParams));
                        examplePromptContent.Append(")");
                    }
                    else
                    {
                        examplePromptContent.Append(")");
                    }

                    examplePromptContent.AppendLine("}}}}");
                    examplePromptContent.AppendLine("```");
                    examplePromptContent.AppendLine();
                }
            }

            var promptName = $"{ServerName}_{resourceName}_examples";
            var promptDescription = $"Example usage patterns for {resourceName} in Google {apiTitle} API";

            prompts.Add(new Prompt(promptName, examplePromptContent.ToString(), promptDescription));

            // Handle sub-resources
            if (resource.Value.TryGetProperty("resources", out var subResources))
            {
                foreach (var subResource in subResources.EnumerateObject())
                {
                    string subResourceName = subResource.Name;
                    var subExamplePromptContent = new System.Text.StringBuilder();

                    subExamplePromptContent.AppendLine(
                        $"# {ServerName} - Examples for working with {resourceName}.{subResourceName}");
                    subExamplePromptContent.AppendLine();
                    subExamplePromptContent.AppendLine(
                        $"Common scenarios for using {resourceName}.{subResourceName} in the {apiTitle} API:");
                    subExamplePromptContent.AppendLine();

                    if (subResource.Value.TryGetProperty("methods", out var subMethods))
                    {
                        foreach (var method in subMethods.EnumerateObject())
                        {
                            string methodName = method.Name;
                            string opId = $"{resourceName}.{subResourceName}.{methodName}";
                            string safeOpId = StringHelpers.SanitizeToolName($"{ServerName}_{opId}");

                            string description = method.Value.TryGetProperty("description", out var methodDesc)
                                ? methodDesc.GetString() ?? methodName
                                : methodName;

                            subExamplePromptContent.AppendLine($"## Using {methodName}");
                            subExamplePromptContent.AppendLine();
                            subExamplePromptContent.AppendLine(description);
                            subExamplePromptContent.AppendLine();
                            subExamplePromptContent.AppendLine("```");

                            // Create example tool call
                            subExamplePromptContent.Append($"{{{{tool.{safeOpId}(");

                            // Add required parameters
                            var requiredSubParams = new List<string>();
                            if (method.Value.TryGetProperty("parameters", out var parameters))
                            {
                                foreach (var param in parameters.EnumerateObject())
                                {
                                    if (param.Value.TryGetProperty("required", out var reqProp) && reqProp.GetBoolean())
                                    {
                                        string paramName = param.Name;
                                        string paramType = param.Value.TryGetProperty("type", out var typeProp)
                                            ? typeProp.GetString() ?? "string"
                                            : "string";

                                        string exampleValue;
                                        if (paramType == "string")
                                        {
                                            exampleValue = $"\"{paramName}_example\"";
                                        }
                                        else if (paramType == "integer" || paramType == "number")
                                        {
                                            exampleValue = "1";
                                        }
                                        else if (paramType == "boolean")
                                        {
                                            exampleValue = "true";
                                        }
                                        else
                                        {
                                            exampleValue = "\"example\"";
                                        }

                                        requiredSubParams.Add($"{paramName}={exampleValue}");
                                    }
                                }
                            }

                            if (requiredSubParams.Count > 0)
                            {
                                subExamplePromptContent.AppendLine();
                                subExamplePromptContent.AppendLine("    " + string.Join(",\n    ", requiredSubParams));
                                subExamplePromptContent.Append(")");
                            }
                            else
                            {
                                subExamplePromptContent.Append(")");
                            }

                            subExamplePromptContent.AppendLine("}}}}");
                            subExamplePromptContent.AppendLine("```");
                            subExamplePromptContent.AppendLine();
                        }
                    }

                    var subPromptName = $"{ServerName}_{resourceName}_{subResourceName}_examples";
                    var subPromptDescription =
                        $"Example usage patterns for {resourceName}.{subResourceName} in Google {apiTitle} API";

                    prompts.Add(new Prompt(subPromptName, subExamplePromptContent.ToString(), subPromptDescription));
                }
            }
        }

        return prompts;
    }
}