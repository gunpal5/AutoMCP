using System.Text;
using System.Text.Json;
using QuickMCP.Abstractions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace QuickMCP.Http;

/// <summary>
/// Class responsible for making HTTP API calls. This class is designed to be
/// reusable and efficient, using a single HttpClient instance for all calls.
/// </summary>
public class HttpApiCaller
{
    private readonly HttpClient _httpClient;
    private IAuthenticator? _authenticator;
    private ILogger? _logger;
    private string _baseUrl;
    private Dictionary<string, string> _defaultPathParams = new Dictionary<string, string>();

    private Dictionary<string, string> _headers = new Dictionary<string, string>();

    /// <summary>
    /// Constructor to initialize the HttpApiCaller with an HttpClient and optional logger.
    /// </summary>
    /// <param name="httpClient">The HttpClient instance to use for making HTTP requests.</param>
    /// <param name="logger">Optional. The logger to use for logging operations.</param>
    public HttpApiCaller(HttpClient httpClient, ILogger? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Sets the base URL for API calls.
    /// </summary>
    /// <param name="baseUrl">The base URL to use for API calls.</param>
    public void SetBaseUrl(string baseUrl)
    {
        _baseUrl = baseUrl;
    }

    /// <summary>
    /// Sets the authenticator used to authenticate HTTP requests.
    /// </summary>
    /// <param name="authenticator">The authenticator instance.</param>
    public void SetAuthenticator(IAuthenticator authenticator)
    {
        _authenticator = authenticator;
    }

    /// <summary>
    /// Sets the logger instance to use for logging operations.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public void SetLogger(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sets the default path parameters for API calls.
    /// </summary>
    /// <param name="defaultPathParams">A dictionary of default path parameters.</param>
    public void SetDefaultPathParams(Dictionary<string, string> defaultPathParams)
    {
        _defaultPathParams = defaultPathParams;
    }

    /// <summary>
    /// Makes an HTTP API call.
    /// </summary>
    /// <param name="method">The HTTP method (GET, POST, etc.).</param>
    /// <param name="url">The relative URL for the API call.</param>
    /// <param name="content">The request body as a dictionary of JSON elements.</param>
    /// <param name="mimeType">The MIME type of the request body.</param>
    /// <param name="query">Query parameters for the API call.</param>
    /// <param name="pathParam">Path parameters for the API call.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the request.</param>
    /// <returns>A task that represents the asynchronous operation, returning a CallToolResponse.</returns>
    public async Task<CallToolResponse> Call(HttpMethod method,
                                            string url,
                                            Dictionary<string, JsonElement>? content = null,
                                            string mimeType = "application/json",
                                            List<KeyValuePair<string, string?>>? query = null,
                                            List<KeyValuePair<string, string?>>? headerParams = null,
                                            Dictionary<string, string?>? pathParam = null,
                                            CancellationToken cancellationToken = default)
    {
        var uri = BuildUri(url, query, pathParam);
        _logger?.LogInformation($"Making {method} request to {url}");

        HttpRequestMessage request = new HttpRequestMessage(method, uri);
        AddContent(request, content, mimeType);

        AddHeaders(request, headerParams);

        if (_authenticator != null)
        {
            await _authenticator.AuthenticateRequestAsync(request);
        }

        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
#if NETSTANDARD2_0_OR_GREATER
        string responseContent = await response.Content.ReadAsStringAsync();
#else
        string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
#endif

        _logger?.LogDebug($"Response from {url}: {responseContent}");

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError($"HTTP error {response.StatusCode} from {url}: {responseContent}");
            return new CallToolResponse()
            {
                IsError = true,
                Content = new List<Content>([
                    new Content()
                    {
                        Type = "text",
                        Text = $"HTTP error {response.StatusCode} from {url}: {responseContent}"
                    }
                ])
            };
        }

        try
        {
            return new CallToolResponse()
            {
                Content = new List<Content>([
                    new Content()
                    {
                        Type = "text",
                        Text = responseContent,
                    }
                ])
            };
        }
        catch (JsonException ex)
        {
            _logger?.LogError(
                $"Error deserializing response from {url}: {ex.Message}. Response was: {responseContent}");
            return new CallToolResponse()
            {
                IsError = true,
                Content = new List<Content>([
                    new Content()
                    {
                        Type = "text",
                        Text =
                            $"Error deserializing response from {url}: {ex.Message}. Response was: {responseContent}"
                    }
                ])
            };
        }
    }

    private void AddHeaders(HttpRequestMessage request, List<KeyValuePair<string, string?>> headerParams)
    {
        if (headerParams == null)
        {
            return;
        }

        foreach (var header in headerParams)
        {
            if (string.IsNullOrEmpty(header.Value))
            {
                continue;
            }
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    /// <summary>
    /// Adds content to an HTTP request with the specified MIME type.
    /// </summary>
    /// <param name="request">The HTTP request message.</param>
    /// <param name="content">The content to add to the request.</param>
    /// <param name="mimeType">The MIME type of the content.</param>
    private void AddContent(HttpRequestMessage request, Dictionary<string, JsonElement>? content, string mimeType)
    {
        if (content != null)
        {
            if (content.ContainsKey("body"))
            {
                content = content["body"].EnumerateObject().ToDictionary(x => x.Name, x => x.Value);
            }

            if (mimeType == "application/x-www-form-urlencoded")
            {
                request.Content = new FormUrlEncodedContent(content.Select(x =>
                    new KeyValuePair<string, string>(x.Key, x.Value.GetRawText())));
            }
            else if (mimeType == "multipart/form-data")
            {
                var multipart = new MultipartFormDataContent();
                foreach (var value in content)
                {
                    multipart.Add(new StringContent(value.Value.GetRawText()), value.Key);
                }

                request.Content = multipart;
            }
            else if (mimeType == "application/json" || string.IsNullOrEmpty(mimeType))
            {
                request.Content =
                    new StringContent(
                        JsonSerializer.Serialize(content,
                            QuickMcpJsonSerializerContext.Default.DictionaryStringJsonElement),
                        Encoding.UTF8, mimeType);

            }
            else throw new Exception($"Unsupported mime type {mimeType}");
        }
    }

    /// <summary>
    /// Builds a complete URI from the base URL, path, query parameters, and path parameters.
    /// </summary>
    /// <param name="url">The relative URL for the API call.</param>
    /// <param name="query">Query parameters for the API call.</param>
    /// <param name="pathParam">Path parameters for the API call.</param>
    /// <returns>The complete URI for the API call.</returns>
    private Uri BuildUri(string url, List<KeyValuePair<string, string?>>? query, Dictionary<string, string?>? pathParam)
    {
        if (_baseUrl.EndsWith("/"))
        {
            _baseUrl = _baseUrl.Substring(0, _baseUrl.Length - 1);
        }

        foreach (var param in _defaultPathParams)
        {
            if (string.IsNullOrEmpty(param.Value)) continue;
            url = url.Replace($"{{{param.Key}}}", param.Value ?? string.Empty);
        }

        if (pathParam != null)
        {
            foreach (var param in pathParam)
            {
                if (string.IsNullOrEmpty(param.Value)) continue;
                url = url.Replace($"{{{param.Key}}}", param.Value ?? string.Empty);
            }
        }

        if (!url.StartsWith($"/"))
        {
            url = $"/{url}";
        }

        var urlBuilder = new UriBuilder(_baseUrl + url);
        if (query != null)
        {
            urlBuilder.Query = string.Join("&",
                query?.Select(
                    kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value ?? string.Empty)}") ??
                Array.Empty<string>());
        }

        return urlBuilder.Uri;
    }
}