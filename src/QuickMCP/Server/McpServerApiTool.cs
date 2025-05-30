using System.Text.Json;
using QuickMCP.Http;
using QuickMCP.Types;
using Microsoft.OpenApi.Models;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;

namespace QuickMCP.Server;

/// <inheritdoc/>
public class McpServerApiTool : McpServerTool
{
    private ToolInfo _toolInfo;
    private HttpApiCaller _caller;

    public McpServerApiTool(ToolInfo info, HttpApiCaller apiCaller)
    {
        if (info == null)
            throw new ArgumentNullException(nameof(info));
        this._toolInfo = info;
        this._caller = apiCaller;
        ProtocolTool = info.ToProtocolTool();
    }
    /// <inheritdoc/>
    public override async ValueTask<CallToolResponse> InvokeAsync(RequestContext<CallToolRequestParams> request,
                                                                  CancellationToken cancellationToken = new CancellationToken())
    {
        string url = _toolInfo.Url;
        Dictionary<string, JsonElement>? requestBody = null;
        var pathParams = new Dictionary<string, string?>();
        var queryParams = new List<KeyValuePair<string, string?>>();
        var headerParams = new List<KeyValuePair<string, string?>>();
        var arguments = request.Params?.Arguments;
        if (arguments != null)
        {
            if (_toolInfo?.Parameters != null && _toolInfo.Parameters.Any())
            {
                // Separate path and query parameters.

                foreach (var p in _toolInfo.Parameters)
                {
                    if (p.In == ParameterLocation.Path.ToString().ToLower())
                    {
                        if (!arguments.TryGetValue(p.Name, out var value))
                        {
                            if (p.Required == true)
                                throw new Exception($"Missing parameter {p.Name}");
                        }
                        else
                        {
                            pathParams.Add(p.Name, value.GetRawText().Replace("\"", ""));
                        }
                    }
                    else if (p.In == ParameterLocation.Query.ToString().ToLower())
                    {
                        if (!arguments.TryGetValue(p.Name, out var value))
                        {
                            if (p.Required == true)
                                throw new Exception($"Missing parameter {p.Name}");
                        }
                        else
                        {
                            if (value.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in value.EnumerateArray())
                                {
                                    queryParams.Add(new KeyValuePair<string, string>($"{p.Name}", item.GetRawText().Replace("\"", "")));
                                }
                            }
                            else
                                queryParams.Add(new KeyValuePair<string, string>(p.Name, value.GetRawText().Replace("\"", "")));
                        }
                    }
                    else if (p.In == ParameterLocation.Header.ToString().ToLower())
                    {
                        if (!arguments.TryGetValue(p.Name, out var value))
                        {
                            if (p.Required == true)
                                throw new Exception($"Missing parameter {p.Name}");
                        }
                        else
                        {
                            if (value.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in value.EnumerateArray())
                                {
                                    headerParams.Add(new KeyValuePair<string, string>($"{p.Name}", item.GetRawText().Replace("\"", "")));
                                }
                            }
                            else
                                headerParams.Add(new KeyValuePair<string, string>(p.Name, value.GetRawText().Replace("\"", "")));
                        }
                    }
                }

                if (_toolInfo.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    if (queryParams.Any())
                    {
                        var query = string.Join("&",
                            queryParams.Select(
                                kv => $"{kv.Key}={System.Net.WebUtility.UrlEncode(kv.Value?.ToString())}"));
                        url += "?" + query;
                    }
                }
                else if (_toolInfo.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                         _toolInfo.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
                         _toolInfo.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase))
                {
                    requestBody = arguments.ToDictionary(s => s.Key, y => y.Value) ?? new Dictionary<string, JsonElement>();
                }
            }
        }

        return await _caller.Call(new HttpMethod(_toolInfo.Method), url, content: requestBody, _toolInfo.MimeType ?? "application/json", queryParams, headerParams, pathParams, cancellationToken);
    }

    public override Tool ProtocolTool { get; }
}