using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace QuickMCP.Types;

/// <summary>
/// Configuration for builder
/// </summary>
public class BuilderConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "openapi";
    /// <summary>
    /// Gets or sets the name of the server.
    /// </summary>
    [JsonPropertyName("serverName")]
    public string? ServerName { get; set; }

    /// <summary>
    /// Gets or sets the description of the server.
    /// </summary>
    [JsonPropertyName("serverDescription")]
    public string? ServerDescription { get; set; }

    /// <summary>
    /// Gets or sets the URL of the API specification.
    /// </summary>
    [JsonPropertyName("apiSpecUrl")]
    public string? ApiSpecUrl { get; set; }

    /// <summary>
    /// Gets or sets the path to the API specification.
    /// </summary>
    [JsonPropertyName("apiSpecPath")]
    public string? ApiSpecPath { get; set; }

    /// <summary>
    /// Gets or sets the base URL for the API.
    /// </summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the paths excluded from processing.
    /// </summary>
    [JsonPropertyName("excludedPaths")]
    public List<string>? ExcludedPaths { get; set; }

    /// <summary>
    /// Gets or sets the paths included for processing.
    /// </summary>
    [JsonPropertyName("includedPaths")]
    public List<string>? IncludedPaths { get; set; }

    /// <summary>
    /// Gets or sets the headers for the server.
    /// </summary>
    [JsonPropertyName("serverHeaders")]
    public Dictionary<string, string>? ServerHeaders { get; set; }

    /// <summary>
    /// Gets or sets the default path parameters for the server.
    /// </summary>
    [JsonPropertyName("defaultPathParameters")]
    public Dictionary<string, string>? DefaultPathParameters { get; set; }

    /// <summary>
    /// Gets or sets the authentication configuration.
    /// </summary>
    [JsonPropertyName("authentication")]
    public AuthConfig? Authentication { get; set; }

    /// <summary>
    /// Specifies whether resources should be generated.
    /// </summary>
    [JsonPropertyName("generateResources")]
    public bool GenerateResources { get; set; } = false;

    /// <summary>
    /// Specifies whether prompts should be generated.
    /// </summary>
    [JsonPropertyName("generatePrompts")]
    public bool GeneratePrompts { get; set; } = false;

    /// <summary>
    /// Host protocol for the server, e.g., "http", "stdio".
    /// </summary>
    [JsonPropertyName("hostProtocol")]
    public string? HostProtocol { get; set; }

    /// <summary>
    /// Gets or sets the metadata file associated with the configuration.
    /// </summary>
    public string? MetadataFile { get; set; }

    /// <summary>
    /// List of External Resources to be included
    /// </summary>
    [JsonPropertyName("externalResources")]
    public List<Resource>? ExternalResources { get; set; }
}