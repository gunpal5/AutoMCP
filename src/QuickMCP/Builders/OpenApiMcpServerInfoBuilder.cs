using System.Text.Json.Nodes;
using QuickMCP.Abstractions;
using QuickMCP.Helpers;
using QuickMCP.Types;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpYaml.Serialization;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace QuickMCP.Builders;

/// <summary>
/// Implementation of IMcpServerBuilder for OpenAPI/Swagger specifications
/// </summary>
public class OpenApiMcpServerInfoBuilder : HttpMcpServerInfoBuilder
{
    private OpenApiDocument? _openApiDocument;

    /// <summary>
    /// Creates a new instance of MCPToolBuilder for OpenAPI specifications
    /// </summary>
    /// <param name="serverName">Name of the server</param>
    /// <param name="loggerFactory">Optional logger factory</param>
    public OpenApiMcpServerInfoBuilder(string serverName = "openapi_tools")
        : base(serverName)
    {
    }

    /// <inheritdoc />
    public override IMcpServerInfoBuilder FromUrl(string url)
    {
        OpenApiUrl = url;
        Logger?.LogInformation("Set OpenAPI URL: {Url}", url);
        return this;
    }

    /// <inheritdoc />
    public override IMcpServerInfoBuilder FromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("OpenAPI specification file not found", filePath);
        }

        OpenApiFilePath = filePath;
        Logger?.LogInformation("Set OpenAPI file path: {FilePath}", filePath);
        return this;
    }

    /// <inheritdoc />
    public override IMcpServerInfoBuilder FromConfiguration(string configPath)
    {
        ConfigFilePath = configPath;
        return base.FromConfiguration(configPath);
    }

    /// <inheritdoc />
    public override async Task<McpServerInfo> BuildAsync()
    {
        try
        {
            // Load OpenAPI specification from the appropriate source
            if (!string.IsNullOrEmpty(ConfigFilePath))
            {
                var config = await LoadConfigurationAsync<BuilderConfig>(ConfigFilePath);

                if (config != null)
                {
                    return await ProcessConfigurationAsync(config);
                }
            }
            else if (!string.IsNullOrEmpty(OpenApiUrl))
            {
                _openApiDocument = await LoadOpenApiFromUrlAsync(OpenApiUrl);
            }
            else if (!string.IsNullOrEmpty(OpenApiFilePath))
            {
                _openApiDocument = await LoadOpenApiFromFileAsync(OpenApiFilePath);
            }

            if (_openApiDocument == null)
            {
                Logger?.LogWarning("No OpenAPI document was loaded. No tools will be registered.");
                return CreateToolCollection();
            }

            ProcessOpenApiDocumentAsync(_openApiDocument);

            // Generate resources if enabled
            if (GenerateResourcesFlag)
            {
                RegisterResources(_openApiDocument);
            }

            // Generate prompts if enabled
            if (GeneratePromptsFlag)
            {
                GeneratePrompts(_openApiDocument);
            }

            return CreateToolCollection();
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error building MCP tools");
            throw;
        }
    }

    /// <summary>
    /// Loads an OpenAPI specification document from the provided URL asynchronously.
    /// </summary>
    /// <param name="url">The URL of the OpenAPI specification to load.</param>
    /// <returns>The loaded <see cref="OpenApiDocument"/> if successful; otherwise, null.</returns>
    private async Task<OpenApiDocument?> LoadOpenApiFromUrlAsync(string url)
    {
        try
        {
            Logger?.LogInformation("Loading OpenAPI spec from URL: {Url}", url);

            var httpResponse = await HttpClient.GetAsync(url);
            if (!httpResponse.IsSuccessStatusCode)
            {
                Logger?.LogError("Failed to fetch OpenAPI spec from URL with status code: {StatusCode}",
                    httpResponse.StatusCode);
                return null;
            }

            var fileContent = await httpResponse.Content.ReadAsStringAsync();
            
            fileContent = ConvertToOpenApi30Json(url,fileContent);
            var result = new OpenApiStringReader().Read(fileContent, out var diagnostic);


            if (diagnostic.Errors.Count > 0)
            {
                foreach (var error in diagnostic.Errors)
                {
                    Logger?.LogWarning("OpenAPI parsing error: {Error}", error);
                }
            }

            Logger?.LogInformation("Successfully loaded OpenAPI spec from URL");
            return result;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load OpenAPI spec from URL: {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Asynchronously loads an OpenAPI document from a file.
    /// </summary>
    /// <param name="filePath">The file path of the OpenAPI specification to load.</param>
    /// <returns>The loaded <see cref="OpenApiDocument"/> if successful; otherwise, null.</returns>
    private async Task<OpenApiDocument?> LoadOpenApiFromFileAsync(string filePath)
    {
        try
        {
            Logger?.LogInformation("Loading OpenAPI spec from file: {FilePath}", filePath);

#if NETSTANDARD2_0_OR_GREATER
            var fileContent = File.ReadAllText(filePath);
#else
            var fileContent = await File.ReadAllTextAsync(filePath);
#endif

            fileContent = ConvertToOpenApi30Json(filePath,fileContent);
            var reader = new OpenApiStringReader();
            var result = reader.Read(fileContent, out var diagnostic);

            if (diagnostic.Errors.Count > 0)
            {
                foreach (var error in diagnostic.Errors)
                {
                    Logger?.LogWarning("OpenAPI parsing error: {Error}", error);
                }
            }

            Logger?.LogInformation("Successfully loaded OpenAPI spec from file");
            return result;
        }
        catch (Exception ex)
        {
            
            
            Logger?.LogError(ex, "Failed to load OpenAPI spec from file: {FilePath}", filePath);
            return null;
        }
    }

    private string ConvertToOpenApi30Json(string inputFile, string input)
    {
        try
        {
            if (OpenApi31Support.IsOpenApi31(input))
            {
                var options = new ConverterOptions
                {
                    Verbose = false,
                    DeleteExampleWithId = true,
                    AllOfTransform = true,
                    ConvertOpenIdConnectToOAuth2 = true,
                    ConvertSchemaComments = true
                };

                // Read input file
                JObject openapiDocument;

                if (inputFile.EndsWith(".yaml") || inputFile.EndsWith(".yml"))
                {
                    var serializer = new SharpYaml.Serialization.Serializer(
                        new SerializerSettings
                        {
                            NamingConvention = new CamelCaseNamingConvention(),
                        });

                    var yamlObject = serializer.Deserialize<object>(input);
                    string json = JsonSerializer.Serialize(yamlObject);
                    openapiDocument = JObject.Parse(json);
                }
                else
                {
                    // Assume JSON
                    openapiDocument = JObject.Parse(input);
                }

                // Create converter and convert
                var converter = new Converter(openapiDocument, options);
                var converted = converter.Convert();

                return JsonConvert.SerializeObject(converted);
            }
            else
            {
                return input;
            }
        }
        catch
        {
            return OpenApi31Support.ConvertToOpenApi30(input);
        }
    }

    /// <summary>
    /// Processes the provided configuration and generates an instance of McpServerInfo.
    /// </summary>
    /// <param name="config">The configuration containing details such as server name, description, and OpenAPI specification paths or URLs.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the configured instance of McpServerInfo.</returns>
    private async Task<McpServerInfo> ProcessConfigurationAsync(BuilderConfig config)
    {
        try
        {
            InitializeFromConfig(config);
            // Set up authentication if configured


            // Process OpenAPI URL if provided
            if (!string.IsNullOrEmpty(config.ApiSpecUrl))
            {
                _openApiDocument = await LoadOpenApiFromUrlAsync(config.ApiSpecUrl);
            }
            else if (!string.IsNullOrEmpty(config.ApiSpecPath))
            {
                _openApiDocument = await LoadOpenApiFromFileAsync(config.ApiSpecPath);
            }

            if (_openApiDocument != null)
            {
                ProcessOpenApiDocumentAsync(_openApiDocument);

                // Generate resources if enabled
                if (GenerateResourcesFlag)
                {
                    RegisterResources(_openApiDocument);
                }

                // Generate prompts if enabled
                if (GeneratePromptsFlag)
                {
                    GeneratePrompts(_openApiDocument);
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


    /// <summary>
    /// Processes the provided OpenAPI document, extracting operations, determining the base URL,
    /// applying filters, and registering tools based on the document content.
    /// </summary>
    /// <param name="openApiDoc">The OpenAPI document to process</param>
    private void ProcessOpenApiDocumentAsync(OpenApiDocument openApiDoc)
    {
        try
        {
            Logger?.LogInformation("Processing OpenAPI document");

            if (openApiDoc.Paths == null || openApiDoc.Paths.Count == 0)
            {
                Logger?.LogWarning("OpenAPI document has no paths");
                return;
            }

            BaseUrl = DetermineServerUrl(openApiDoc);

            if (string.IsNullOrEmpty(ServerName) || ServerName == "openapi")
            {
                ServerName = openApiDoc.Info?.Title ?? "MCP Server";
            }

            if (string.IsNullOrEmpty(ServerDescription))
            {
                ServerDescription = openApiDoc.Info?.Description ?? $"{ServerName} MCP Server";
            }

            var operationsInfo = ExtractOperationsInfo(openApiDoc);

            // Apply filters
            var filteredOps = FilterOperations(operationsInfo);

            Logger?.LogInformation("Registering {Count} tools after filtering", filteredOps.Count);

            foreach (var kvp in filteredOps)
            {
                var opId = kvp.Key;
                var info = kvp.Value;
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
                    Logger?.LogError(ex, "Failed to register tool for operation {OperationId}", opId);
                }
            }

            Logger?.LogInformation("Successfully processed OpenAPI document");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to process OpenAPI document");
            throw;
        }
    }

    /// <summary>
    /// Determines the server URL from the provided OpenAPI document. If a base URL is already set, it will be returned;
    /// otherwise, it extracts the first server URL from the document. Throws an exception if no server URL is found.
    /// </summary>
    /// <param name="openApiDoc">The OpenAPI document from which the server URL is extracted.</param>
    /// <returns>The determined server URL as a string.</returns>
    /// <exception cref="Exception">Thrown when no server URL is found in the OpenAPI document.</exception>
    private string DetermineServerUrl(OpenApiDocument openApiDoc)
    {
        // Extract server URL from the OpenAPI document
        if (!string.IsNullOrEmpty(BaseUrl))
            return BaseUrl;
        if (openApiDoc.Servers != null && openApiDoc.Servers.Count > 0)
        {
            return openApiDoc.Servers[0].Url;
        }

        throw new Exception("No server URL found in OpenAPI document, please specify a base URL.");
    }

    /// <summary>
    /// Extracts operations' metadata from an OpenAPI document.
    /// </summary>
    /// <param name="openApiDoc">The OpenAPI document from which operations will be extracted.</param>
    /// <returns>
    /// A dictionary containing operation IDs as keys and their corresponding <c>OperationInfo</c> as values.
    /// </returns>
    private Dictionary<string, OperationInfo> ExtractOperationsInfo(OpenApiDocument openApiDoc)
    {
        var operationsInfo = new Dictionary<string, OperationInfo>();

        foreach (var path in openApiDoc.Paths)
        {
            foreach (var operation in path.Value.Operations)
            {
                var method = operation.Key.ToString().ToUpperInvariant();
                var op = operation.Value;

                // Generate operation ID
                var opId = op.OperationId ??
                           $"{method}_{path.Key.Replace("/", "_").Replace("{", "").Replace("}", "")}";
                var sanitizedOpId = StringHelpers.SanitizeToolName(opId);

                // Extract parameters
                var parameters = ExtractParameters(op);

                // Extract response schema
                var responseSchema = ExtractResponseSchema(op);


                // Create operation info
                operationsInfo[sanitizedOpId] = new OperationInfo
                {
                    Summary = op.Description ?? op.Summary ?? sanitizedOpId,
                    Parameters = parameters,
                    Path = path.Key,
                    Method = method,
                    ResponseSchema = responseSchema,
                    Tags = op.Tags?.Select(t => t.Name).ToList() ?? new List<string>()
                };
            }
        }

        return operationsInfo;
    }

    /// <summary>
    /// Extracts a list of parameters from the provided OpenAPI operation.
    /// </summary>
    /// <param name="operation">The OpenAPI operation from which to extract parameters.</param>
    /// <returns>A list of parameters defined in the OpenAPI operation.</returns>
    private List<Parameter> ExtractParameters(OpenApiOperation operation)
    {
        var parameters = new List<Parameter>();

        // Process path and query parameters
        if (operation.Parameters != null)
        {
            foreach (var param in operation.Parameters)
            {
                parameters.Add(new Parameter
                {
                    Name = param.Name,
                    In = param.In?.ToString().ToLowerInvariant(),
                    Required = param.Required,
                    Schema = ConvertOpenApiSchemaToJsonNode(param.Schema),
                    Description = param.Description ?? ""
                });
            }
        }

        // Process request body if present
        if (operation.RequestBody != null)
        {
            var requestBody = operation.RequestBody;
            var bodySchema = new JsonObject();
            string? contentType = null;
            if (requestBody.Content.TryGetValue("application/json", out var mediaType) &&
                mediaType.Schema != null)
            {
                contentType = "application/json";
                bodySchema = ConvertOpenApiSchemaToJsonNode(mediaType.Schema);
            }
            else if (requestBody.Content.TryGetValue("multipart/form", out var multipartForm))
            {
                contentType = "multipart/form";
                bodySchema = new JsonObject();
                foreach (var prop in multipartForm.Schema?.Properties)
                {
                    bodySchema[prop.Key] = ConvertOpenApiSchemaToJsonNode(prop.Value);
                }
            }
            else if (requestBody.Content.TryGetValue("application/x-www-form-urlencoded", out var urlencode))
            {
                contentType = "application/x-www-form-urlencoded";
                bodySchema = new JsonObject();
                foreach (var prop in urlencode.Schema.Properties)
                {
                    bodySchema[prop.Key] = ConvertOpenApiSchemaToJsonNode(prop.Value);
                }
            }

            parameters.Add(new Parameter
            {
                Name = "body",
                In = "body",
                Required = requestBody.Required,
                Schema = bodySchema,
                Description = "Request body",
                ContentType = contentType
            });
        }

        return parameters;
    }

    /// <summary>
    /// Extracts the response schema from the given OpenAPI operation.
    /// </summary>
    /// <param name="operation">The OpenAPI operation from which the response schema will be extracted.</param>
    /// <returns>A <see cref="JsonNode"/> representation of the response schema, or an empty <see cref="JsonObject"/> if no schema is defined.</returns>
    private JsonNode? ExtractResponseSchema(OpenApiOperation operation)
    {
        if (operation.Responses != null &&
            operation.Responses.TryGetValue("200", out var response) &&
            response.Content != null && response.Content.Count > 0)
        {
            var mediaType = response.Content.FirstOrDefault().Value;
            if (mediaType.Schema != null)
                return ConvertOpenApiSchemaToJsonNode(mediaType.Schema);
        }

        return new JsonObject();
    }

    /// <summary>
    /// Converts an OpenAPI schema into a JSON node structure.
    /// </summary>
    /// <param name="schema">The OpenAPI schema to be converted. Can be null.</param>
    /// <returns>A JSON object representing the structure of the provided OpenAPI schema.</returns>
    private JsonObject ConvertOpenApiSchemaToJsonNode(OpenApiSchema? schema)
    {
        if (schema == null)
        {
            return new JsonObject();
        }

        var result = new JsonObject();
        result["type"] = schema.Type; //.ToIdentifiers()?.FirstOrDefault() ?? "object";

        if (schema.Description != null)
        {
            result["description"] = schema.Description;
        }

        if (schema.Properties != null && schema.Properties.Count > 0)
        {
            var properties = new JsonObject();
            foreach (var prop in schema.Properties)
            {
                properties[prop.Key] = ConvertOpenApiSchemaToJsonNode(prop.Value);
            }

            result["properties"] = properties;
        }

        if (schema.Items != null)
        {
            result["items"] = ConvertOpenApiSchemaToJsonNode(schema.Items);
        }

        if (schema.Required != null && schema.Required.Count > 0)
        {
            var requiredArray = new JsonArray();
            foreach (var req in schema.Required)
            {
                requiredArray.Add(req);
            }

            result["required"] = requiredArray;
        }

        // Handle enum values
        if (schema.Enum != null && schema.Enum.Count > 0)
        {
            var enumArray = new JsonArray();
            foreach (var enumValue in schema.Enum)
            {
                if (enumValue != null)
                {
                    enumArray.Add(enumValue.ToString());
                }
            }

            result["enum"] = enumArray;
        }

        return result;
    }

    /// <summary>
    /// Generates metadata for a tool based on the provided operation ID and operation information.
    /// </summary>
    /// <param name="opId">The unique identifier of the operation.</param>
    /// <param name="info">Detailed information about the operation, including parameters, tags, and response schema.</param>
    /// <returns>An instance of <see cref="ToolMetadata"/> containing the generated metadata for the tool.</returns>
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

            if (param.Required || param.In == "body")
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
        tags.Add("openapi");

        var metadata = new ToolMetadata()
        {
            InputSchema = schema,
            Tags = tags
        };

        if (info.ResponseSchema != null)
        {
            metadata.ResponseSchema = info.ResponseSchema;
        }
        else
        {
        }

        metadata.Parameters = info.Parameters;
        return metadata;
    }

    /// <summary>
    /// Registers resources from the provided OpenAPI document by converting each schema into a structured resource format.
    /// </summary>
    /// <param name="openApiDoc">The OpenAPI document containing schemas to be registered as resources.</param>
    private void RegisterResources(OpenApiDocument openApiDoc)
    {
        try
        {
            // This method would register component schemas as resources
            if (openApiDoc.Components?.Schemas == null || openApiDoc.Components.Schemas.Count == 0)
            {
                Logger?.LogInformation("No schemas found in OpenAPI document for resource generation");
                return;
            }

            Logger?.LogInformation("Registering {Count} resources from OpenAPI schemas",
                openApiDoc.Components.Schemas.Count);

            foreach (var item in openApiDoc.Components.Schemas)
            {
                var schemaName = item.Key;
                var schema = item.Value;
                // Prefix resource name with server name
                var prefixedName = $"{ServerName}_{schemaName}";
                var safeName = StringHelpers.SanitizeName(prefixedName);

                var schemaNode = ConvertOpenApiSchemaToJsonNode(schema);
                var resourceDescription = $"[{ServerName}] {schema.Description ?? $"Resource for {schemaName}"}";

                RegisteredResources[safeName] = new ResourceInfo
                {
                    Schema = schemaNode,
                    Metadata = new Dictionary<string, object>
                    {
                        ["name"] = safeName,
                        ["description"] = resourceDescription,
                        ["serverInfo"] = new Dictionary<string, string> { ["name"] = ServerName },
                        ["tags"] = new List<string> { "resource", ServerName }
                    }
                };
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to register resources from OpenAPI document");
        }
    }

    /// <summary>
    /// Generates prompts from an OpenAPI document, including general usage prompts and example prompts for resources.
    /// </summary>
    /// <param name="openApiDoc">The OpenAPI document used as a source for generating prompts.</param>
    private void GeneratePrompts(OpenApiDocument openApiDoc)
    {
        try
        {
            Logger?.LogInformation("Generating prompts from OpenAPI document");

            // Generate general usage prompt
            var generalPrompt = GenerateGeneralUsagePrompt(openApiDoc);
            Prompts[generalPrompt.Name] = generalPrompt;

            // Generate example prompts for resources
            var examplePrompts = GenerateExamplePrompts(openApiDoc);
            foreach (var prompt in examplePrompts)
            {
                Prompts[prompt.Name] = prompt;
            }

            Logger?.LogInformation("Generated {Count} prompts", Prompts.Count);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error generating prompts");
        }
    }

    /// <summary>
    /// Generates a general usage prompt for an API based on the provided OpenAPI document.
    /// </summary>
    /// <param name="openApiDoc">The OpenAPI document containing API information and structure.</param>
    /// <returns>A prompt containing general usage guidance for the API.</returns>
    private Prompt GenerateGeneralUsagePrompt(OpenApiDocument openApiDoc)
    {
        var info = openApiDoc.Info;
        var apiTitle = info?.Title ?? "API";
        var generalPromptContent = new System.Text.StringBuilder();

        generalPromptContent.AppendLine($"# {ServerName} - API Usage Guide for {apiTitle}");
        generalPromptContent.AppendLine();
        generalPromptContent.AppendLine($"This API provides the following capabilities:");
        generalPromptContent.AppendLine();

        if (openApiDoc.Paths != null)
        {
            foreach (var kvp in openApiDoc.Paths)
            {
                var path = kvp.Key;
                var pathItem = kvp.Value;
                foreach (var op in pathItem.Operations)
                {
                    var methodEnum = op.Key;
                    var operation = op.Value;

                    var method = methodEnum.ToString().ToLowerInvariant();
                    if (new[] { "get", "post", "put", "delete", "patch" }.Contains(method))
                    {
                        var rawToolName = operation.OperationId ?? $"{method}_{path}";
                        var toolName = $"{ServerName}_{rawToolName}";
                        var sanitizedToolName = StringHelpers.SanitizeToolName(toolName);

                        generalPromptContent.AppendLine($"## {sanitizedToolName}");
                        generalPromptContent.AppendLine($"- Path: `{path}` (HTTP {method.ToUpperInvariant()})");
                        generalPromptContent.AppendLine(
                            $"- Description: {operation.Description ?? operation.Summary ?? "No description"}");

                        if (operation.Parameters?.Count > 0)
                        {
                            generalPromptContent.AppendLine("- Parameters:");
                            foreach (var param in operation.Parameters)
                            {
                                var required = param.Required ? "Required" : "Optional";
                                generalPromptContent.AppendLine(
                                    $"  - `{param.Name}` ({param.In}): {param.Description ?? "No description"} [{required}]");
                            }
                        }

                        generalPromptContent.AppendLine();
                    }
                }
            }
        }

        var promptName = $"{ServerName}_api_general_usage";
        var promptDescription = $"General guidance for using {apiTitle} API";

        return new Prompt(promptName, generalPromptContent.ToString(), promptDescription);
    }

    /// <summary>
    /// Generates example prompts for CRUD operations on resources defined in the OpenAPI document.
    /// </summary>
    /// <param name="openApiDoc">The OpenAPI document containing the resource and operation definitions.</param>
    /// <returns>A list of prompts, each representing example usage patterns for a particular resource.</returns>
    private List<Prompt> GenerateExamplePrompts(OpenApiDocument openApiDoc)
    {
        var prompts = new List<Prompt>();
        var crudOps = IdentifyCrudOperations(openApiDoc);

        foreach (var kvp in crudOps)
        {
            var resource = kvp.Key;
            var operations = kvp.Value;
            var examplePromptContent = new System.Text.StringBuilder();

            examplePromptContent.AppendLine($"# {ServerName} - Examples for working with {resource}");
            examplePromptContent.AppendLine();
            examplePromptContent.AppendLine($"Common scenarios for handling {resource} resources:");
            examplePromptContent.AppendLine();

            if (operations.TryGetValue("list", out var listOp))
            {
                var prefixedOp = $"{ServerName}_{listOp}";
                examplePromptContent.AppendLine($"## Listing {resource} resources");
                examplePromptContent.AppendLine();
                examplePromptContent.AppendLine($"To list all {resource} resources:");
                examplePromptContent.AppendLine("```");
                examplePromptContent.AppendLine($"{{{{tool.{prefixedOp}()}}}}");
                examplePromptContent.AppendLine("```");
                examplePromptContent.AppendLine();
            }

            if (operations.TryGetValue("get", out var getOp))
            {
                var prefixedOp = $"{ServerName}_{getOp}";
                examplePromptContent.AppendLine($"## Getting a specific {resource}");
                examplePromptContent.AppendLine();
                examplePromptContent.AppendLine($"To retrieve a specific {resource} by ID:");
                examplePromptContent.AppendLine("```");
                examplePromptContent.AppendLine($"{{{{tool.{prefixedOp}(id=\"example-id\")}}}}");
                examplePromptContent.AppendLine("```");
                examplePromptContent.AppendLine();
            }

            if (operations.TryGetValue("create", out var createOp))
            {
                var prefixedOp = $"{ServerName}_{createOp}";
                examplePromptContent.AppendLine($"## Creating a new {resource}");
                examplePromptContent.AppendLine();
                examplePromptContent.AppendLine($"To create a new {resource}:");
                examplePromptContent.AppendLine("```");
                examplePromptContent.AppendLine($"{{{{tool.{prefixedOp}(");
                examplePromptContent.AppendLine($"    name=\"Example name\",");
                examplePromptContent.AppendLine($"    description=\"Example description\"");
                examplePromptContent.AppendLine($"    # Add other required fields");
                examplePromptContent.AppendLine(")}}}}");
                examplePromptContent.AppendLine("```");
                examplePromptContent.AppendLine();
            }

            var promptName = $"{ServerName}_{resource}_examples";
            var promptDescription = $"Example usage patterns for {resource} resources";

            prompts.Add(new Prompt(promptName, examplePromptContent.ToString(), promptDescription));
        }

        return prompts;
    }

    /// <summary>
    /// Identifies CRUD operations for each resource defined in the OpenAPI document.
    /// </summary>
    /// <param name="openApiDoc">The OpenAPI document containing path and operation data.</param>
    /// <returns>A dictionary mapping resource names to their respective CRUD operations and associated operation identifiers.</returns>
    private Dictionary<string, Dictionary<string, string>> IdentifyCrudOperations(OpenApiDocument openApiDoc)
    {
        var crudOps = new Dictionary<string, Dictionary<string, string>>();

        if (openApiDoc.Paths == null)
        {
            return crudOps;
        }

        foreach (var kvp in openApiDoc.Paths)
        {
            var path = kvp.Key;
            var pathItem = kvp.Value;
            var pathParts = path.Split('/')
                .Where(p => !string.IsNullOrEmpty(p) && !p.StartsWith("{"))
                .ToList();

            if (pathParts.Count == 0)
            {
                continue;
            }

            var resource = StringHelpers.SingularizeResource(pathParts[^1]);
            if (!crudOps.ContainsKey(resource))
            {
                crudOps[resource] = new Dictionary<string, string>();
            }

            foreach (var ops in pathItem.Operations)
            {
                var methodEnum = ops.Key;
                var operation = ops.Value;
                var method = methodEnum.ToString().ToLowerInvariant();
                var opId = StringHelpers.SanitizeToolName(
                    operation.OperationId ?? $"{method}_{path.Replace("/", "_").Replace("{", "").Replace("}", "")}"
                );

                if (method == "get")
                {
                    if (path.Contains("{") && path.Contains("}"))
                    {
                        crudOps[resource]["get"] = opId;
                    }
                    else
                    {
                        crudOps[resource]["list"] = opId;
                    }
                }
                else if (method == "post")
                {
                    crudOps[resource]["create"] = opId;
                }
                else if (method == "put" || method == "patch")
                {
                    crudOps[resource]["update"] = opId;
                }
                else if (method == "delete")
                {
                    crudOps[resource]["delete"] = opId;
                }
            }
        }

        return crudOps;
    }
}