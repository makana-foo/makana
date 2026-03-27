using System.Globalization;
using System.Text.Json.Serialization;

namespace Makana.WebConsole.Services;

internal interface IWebConsoleApiTokenExchangeService
{
    Task<WebConsoleSessionAccessToken?> ExchangeAsync(string idToken, CancellationToken cancellationToken);
}

internal sealed record WebConsoleSessionAccessToken(string AccessToken, DateTimeOffset ExpiresAt);

internal sealed class WebConsoleApiTokenExchangeService(
    HttpClient httpClient,
    ILogger<WebConsoleApiTokenExchangeService> logger) : IWebConsoleApiTokenExchangeService
{
    public async Task<WebConsoleSessionAccessToken?> ExchangeAsync(string idToken, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/auth/web-console/token",
            new WebConsoleApiTokenExchangeRequest(idToken),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "Web console API session token exchange failed with status {StatusCode}. Body: {Body}",
                (int)response.StatusCode,
                errorBody);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<WebConsoleApiTokenExchangeResponse>(cancellationToken);
        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.AccessToken) ||
            !DateTimeOffset.TryParse(
                payload.ExpiresAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var expiresAt))
        {
            logger.LogWarning("Web console API session token exchange returned an invalid payload.");
            return null;
        }

        return new WebConsoleSessionAccessToken(payload.AccessToken, expiresAt);
    }

    private sealed record WebConsoleApiTokenExchangeRequest(
        [property: JsonPropertyName("idToken")] string IdToken);

    private sealed record WebConsoleApiTokenExchangeResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("expiresAt")] string ExpiresAt);
}
