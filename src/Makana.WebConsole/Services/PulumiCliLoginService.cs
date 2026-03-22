using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Makana.WebConsole.Services;

public sealed class PulumiCliLoginService(HttpClient httpClient, ILogger<PulumiCliLoginService> logger)
{
    private const string GrantTypeTokenExchange = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string SubjectTokenTypeIdToken = "urn:ietf:params:oauth:token-type:id_token";
    private const string RequestedTokenTypeAccessToken = "urn:ietf:params:oauth:token-type:access_token";

    public async Task<string?> ExchangeIdTokenAsync(string idToken, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = GrantTypeTokenExchange,
            ["subject_token_type"] = SubjectTokenTypeIdToken,
            ["requested_token_type"] = RequestedTokenTypeAccessToken,
            ["subject_token"] = idToken,
            ["scope"] = "user",
            ["expiration"] = "86400"
        };

        using var response = await httpClient.PostAsync(
            "/api/oauth/token",
            new FormUrlEncodedContent(form),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "CLI token exchange failed with status {StatusCode}. Body: {Body}",
                (int)response.StatusCode,
                errorBody);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<TokenExchangeResponse>(cancellationToken);
        return payload?.AccessToken;
    }

    public static void ConfigureHttpClient(HttpClient client, IConfiguration configuration)
    {
        var baseUrl =
            configuration["PULUMI_API_SERVICE"] ??
            "http://api";

        client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
    }

    private sealed record TokenExchangeResponse(
        [property: JsonPropertyName("access_token")] string AccessToken);
}
