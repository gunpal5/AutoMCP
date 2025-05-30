using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace QuickMCP.Types;

public class ToolInfo
{
    /// <summary>
    /// Gets or sets the name of the tool.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL of the tool.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the HTTP method for the tool's endpoint.
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of parameters for the tool.
    /// </summary>
    [JsonPropertyName("parameters")]
    public List<Parameter> Parameters { get; set; } = new List<Parameter>();

    /// <summary>
    /// Gets or sets the MIME type of the tool's request content.
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets the metadata associated with the tool.
    /// </summary>
    [JsonPropertyName("metadata")]
    public ToolMetadata Metadata { get; set; } = new ToolMetadata();

    /// <summary>
    /// Converts the current ToolInfo into a Protocol Tool instance.
    /// </summary>
    /// <returns>A <see cref="Tool"/> object representing the protocol tool.</returns>
    public Tool ToProtocolTool()
    {
        return new Tool()
        {
            Name = Name,
            Description = Metadata.Description,
            Annotations = new ToolAnnotations()
            {
                OpenWorldHint = true,
                Title = Metadata.Description
            },
            InputSchema = Metadata.InputSchema is null ? new JsonElement() : JsonSerializer.Deserialize<JsonElement>(Metadata.InputSchema.ToJsonString(), QuickMcpJsonSerializerContext.Default.JsonElement)
        };
    }
}