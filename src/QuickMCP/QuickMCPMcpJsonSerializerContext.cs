using System.Text.Json;
using System.Text.Json.Serialization;
using QuickMCP.Types;

namespace QuickMCP;

[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(BuilderConfig))]
[JsonSerializable(typeof(McpServerInfo))]
[JsonSerializable(typeof(ToolInfo))]
[JsonSerializable(typeof(List<ToolInfo>))]
[JsonSerializable(typeof(AuthConfig))]
[JsonSerializable(typeof(Dictionary<string,string>))]
[JsonSerializable(typeof(OperationInfo))]
[JsonSerializable(typeof(MetadataUpdateConfig))]
[JsonSerializable(typeof(List<UpdatedParameterMetadata>))]
[JsonSerializable(typeof(UpdatedParameterMetadata))]
[JsonSerializable(typeof(List<UpdatedToolMetadata>))]
[JsonSerializable(typeof(UpdatedToolMetadata))]
[JsonSerializable(typeof(ServerConfiguration))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class QuickMcpJsonSerializerContext:JsonSerializerContext
{
    
}