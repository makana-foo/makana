using System.Globalization;
using System.Text.Json.Serialization;

namespace Makana.WebConsole.Services;

internal interface IWebConsoleApiTokenExchangeService
{
    Task<WebConsoleSessionAccessToken?> ExchangeAsync(string idToken, string? currentApiToken, CancellationToken cancellationToken);
}

internal sealed record WebConsoleSessionAccessToken(string AccessToken, DateTimeOffset ExpiresAt);

internal sealed class WebConsoleApiTokenExchangeService(
    HttpClient httpClient,
    ILogger<WebConsoleApiTokenExchangeService> logger) : IWebConsoleApiTokenExchangeService
{
    public async Task<WebConsoleSessionAccessToken?> ExchangeAsync(string idToken, string? currentApiToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/web-console/token")
        {
            Content = JsonContent.Create(new WebConsoleApiTokenExchangeRequest(idToken))
        };
        if (!string.IsNullOrWhiteSpace(currentApiToken))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"token {currentApiToken}");
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);

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
