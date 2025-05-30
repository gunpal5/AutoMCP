using System.Net.Http.Headers;
using System.Text.Json;
using QuickMCP.Abstractions;
using QuickMCP.Types;

namespace QuickMCP.Authentication;

/// <summary>
/// Provides OAuth 2.0 client credentials flow authentication.
/// This authenticator retrieves and caches access tokens to authenticate HTTP requests.
/// </summary>
public class OAuth2ClientCredentialsAuthenticator : IAuthenticator
{
    #region Fields and Properties

    private readonly string _tokenUrl;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _scope;
    private readonly HttpClient _httpClient;
    private readonly OAuthCache _tokenCache;
    public string Type => Metadata.Type;
    public AuthenticatorMetadata Metadata => GetMetadata();

    #endregion

    #region Factory

    /// <summary>
    /// Retrieves the metadata for the API key authenticator, including its name, description,
    /// configuration keys, and type.
    /// </summary>
    /// <returns>An instance of <see cref="AuthenticatorMetadata"/> containing details about the API key authenticator.</returns>
    public static AuthenticatorMetadata GetMetadata()
    {
        const string name = "OAuth 2.0 Client Credentials Authentication";

        const string description =
            $"OAuth 2.0 Client Credentials Authentication using token endpoint.";

        const string type = "oAuth";

        List<(string Key, string Description, bool IsRequired)> configKeys =
        [
            ("tokenUrl", "The URL used to retrieve an access token.", true),
            ("clientId", "The client ID for authentication.", true),
            ("clientSecret", "The client secret for authentication.", true),
            ("scope", "Optional access scope, defaults to 'client_credentials'.", false)
        ];
        return new AuthenticatorMetadata(name, description, configKeys, type);
    }

    /// <summary>
    /// Creates an OAuth2 Client Credentials Authenticator.
    /// </summary>
    /// <param name="settings">The settings containing token URL, client ID, client secret, and optional scope.</param>
    /// <returns>An instance of <see cref="OAuth2ClientCredentialsAuthenticator"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when required settings (token URL, client ID, client secret) are missing.</exception>
    public static IAuthenticator Create(Dictionary<string, string?> settings)
    {
        if (!settings.TryGetValue("tokenUrl", out var tokenUrl) ||
            !settings.TryGetValue("clientId", out var clientId) ||
            !settings.TryGetValue("clientSecret", out var clientSecret))
        {
            throw new ArgumentException(
                "OAuth2 authentication requires 'tokenUrl', 'clientId', and 'clientSecret' settings");
        }

        if (string.IsNullOrEmpty(tokenUrl) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            throw new ArgumentException(
                "OAuth2 authentication requires 'tokenUrl', 'clientId', and 'clientSecret' settings");
        }
        settings.TryGetValue("scope", out var scope);

        return new OAuth2ClientCredentialsAuthenticator(tokenUrl!, clientId!, clientSecret!, scope);
    }

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="OAuth2ClientCredentialsAuthenticator"/> class.
    /// </summary>
    /// <param name="tokenUrl">The URL used to retrieve an access token.</param>
    /// <param name="clientId">The client ID for authentication.</param>
    /// <param name="clientSecret">The client secret for authentication.</param>
    /// <param name="scope">Optional access scope, defaults to "api".</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="tokenUrl"/>, <paramref name="clientId"/>, or <paramref name="clientSecret"/> is null.
    /// </exception>
    public OAuth2ClientCredentialsAuthenticator(
        string tokenUrl,
        string clientId,
        string clientSecret,
        string? scope = null)
    {
        _tokenUrl = tokenUrl ?? throw new ArgumentNullException(nameof(tokenUrl));
        _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _clientSecret = clientSecret ?? throw new ArgumentNullException(nameof(clientSecret));
        _scope = scope ?? "api";
        _httpClient = new HttpClient();
        _tokenCache = new OAuthCache();
    }

    #endregion

    #region IAuthenticator Implementation

    /// <inheritdoc />
    public async Task AuthenticateRequestAsync(HttpRequestMessage request)
    {
        var token = await GetAccessTokenAsync();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> GetAuthHeadersAsync()
    {
        var token = await GetAccessTokenAsync();
        if (!string.IsNullOrEmpty(token))
        {
            return new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {token}"
            };
        }

        return new Dictionary<string, string>();
    }

    /// <inheritdoc />
    public async Task<bool> IsAuthenticatedAsync()
    {
        var token = await GetAccessTokenAsync();
        return !string.IsNullOrEmpty(token);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Retrieves an access token, utilizing the cache if available, otherwise fetching a new one.
    /// </summary>
    /// <returns>The access token or null if unavailable.</returns>
    private async Task<string?> GetAccessTokenAsync()
    {
        // Check cache first
        var cachedToken = _tokenCache.GetToken();
        if (cachedToken != null)
        {
            return cachedToken;
        }

        try
        {
            // Build request for token
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("client_secret", _clientSecret),
                new KeyValuePair<string, string>("scope", _scope)
            });

            // Request new token
            var response = await _httpClient.PostAsync(_tokenUrl, content);
            response.EnsureSuccessStatusCode();

            var tokenJson = await response.Content.ReadAsStringAsync();
            var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenJson);

            var accessToken = tokenData.GetProperty("access_token").GetString();
            var expiresIn = tokenData.TryGetProperty("expires_in", out var expiresInElement)
                ? expiresInElement.GetInt32()
                : 3600;

            if (accessToken != null)
            {
                _tokenCache.SetToken(accessToken, expiresIn);
                return accessToken;
            }
        }
        catch (Exception)
        {
            throw;
        }

        return null;
    }

    #endregion
}