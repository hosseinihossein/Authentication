using System.Text.Json;
using System.Text.Json.Serialization;

namespace AspApp.Helpers;

public class TurnstileService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _secretKey;
    private const string SiteverifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    private readonly bool _turnstileEnabled;


    public TurnstileService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _secretKey = configuration["TurnstileSecretKey"]!;
        _turnstileEnabled = configuration.GetValue<bool>("TurnsTileEnable", false);
    }

    public async Task<TurnstileResponse?> ValidateTokenAsync(HttpContext httpContext, string? token = null)
    {
        if (!_turnstileEnabled)
        {
            return new TurnstileResponse()
            {
                Success = true,
            };
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return new TurnstileResponse()
            {
                Success = false,
                ErrorCodes = new[] { "token-null" }
            };
        }

        var remoteip = httpContext.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ??
        httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault() ??
        httpContext.Connection.RemoteIpAddress?.ToString();

        var parameters = new Dictionary<string, string>
        {
            { "secret", _secretKey },
            { "response", token }
        };

        if (!string.IsNullOrEmpty(remoteip))
        {
            parameters.Add("remoteip", remoteip);
        }

        var postContent = new FormUrlEncodedContent(parameters);
        /*var formData = new
        {
            secret = _secretKey,
            response = token
        };*/

        try
        {
            var _httpClient = _httpClientFactory.CreateClient();
            var response = await _httpClient.PostAsync(SiteverifyUrl, postContent);
            //var response = await _httpClient.PostAsJsonAsync(SiteverifyUrl, formData);
            var stringContent = await response.Content.ReadAsStringAsync();

            TurnstileResponse? turnstileResponse =
            JsonSerializer.Deserialize<TurnstileResponse>(stringContent);

            return turnstileResponse;
        }
        catch (Exception)
        {
            return new TurnstileResponse
            {
                Success = false,
                ErrorCodes = new[] { "server-internal-error" }
            };
        }
    }
}

public class TurnstileResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error-codes")]
    public string[] ErrorCodes { get; set; } = [];
}