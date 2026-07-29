using System.Text.Json;
using System.Text.Json.Serialization;

namespace AspApp.Helpers;

public class TurnstileService
{
    readonly IHttpClientFactory _httpClientFactory;
    readonly string _secretKey;
    const string SiteverifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public TurnstileService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _secretKey = configuration["TurnstileSecretKey"]!;
    }

    public async Task<TurnstileResponse?> ValidateTokenAsync(string token, string? remoteip = null)
    {
        var parameters = new Dictionary<string, string>
        {
            { "secret", _secretKey },
            { "response", token }
        };

        if (!string.IsNullOrEmpty(remoteip))
        {
            parameters.Add("remoteip", remoteip);
        }

        //var postContent = new FormUrlEncodedContent(parameters);
        var formData = new
        {
            secret = _secretKey,
            response = token
        };

        try
        {
            var _httpClient = _httpClientFactory.CreateClient();
            //var response = await _httpClient.PostAsync(SiteverifyUrl, postContent);
            var response = await _httpClient.PostAsJsonAsync(SiteverifyUrl, formData);
            var stringContent = await response.Content.ReadAsStringAsync();

            //Console.WriteLine(stringContent);

            TurnstileResponse? turnstileResponse =
            JsonSerializer.Deserialize<TurnstileResponse>(stringContent);

            //Console.WriteLine("\n***** turnstileResponse?.Success: " + turnstileResponse?.Success);
            //Console.WriteLine("\n***** turnstileResponse?.ErrorCodes.Length: " + turnstileResponse?.ErrorCodes.Length);

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