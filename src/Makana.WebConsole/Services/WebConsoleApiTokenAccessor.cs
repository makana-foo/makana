using System.Globalization;
using Makana.WebConsole.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Makana.WebConsole.Services;

internal sealed class WebConsoleApiTokenAccessor(
    IHttpContextAccessor httpContextAccessor,
    IHostEnvironment environment,
    IOptions<WebConsoleApiOptions> apiOptions,
    IWebConsoleApiTokenExchangeService tokenExchangeService)
{
    internal const string ApiTokenName = "makana_api_token";
    internal const string ApiTokenExpiresAtName = "makana_api_token_expires_at";

    private const string CachedItemKey = "Makana.WebConsole.ApiToken";
    private static readonly TimeSpan ExpirationSkew = TimeSpan.FromMinutes(1);

    public async Task<string?> GetApiTokenAsync(CancellationToken cancellationToken)
        => (await GetApiTokenInfoAsync(cancellationToken))?.AccessToken;

    public async Task<WebConsoleApiTokenInfo?> GetApiTokenInfoAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsProduction() && apiOptions is { Value.ApiToken: string apiToken })
        {
            return new WebConsoleApiTokenInfo(apiToken, null);
        }

        if (httpContextAccessor is not { HttpContext: { Items: { } scopeData } httpContext })
        {
            return null;
        }

        if (scopeData.TryGetValue(CachedItemKey, out var cached) && cached is WebConsoleApiTokenInfo cachedToken)
        {
            return cachedToken;
        }

        var authResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (authResult is { Succeeded: true, Principal.Identity.IsAuthenticated: true })
        {
            if (TryGetStoredApiToken(authResult.Properties) is { } storedToken)
            {
                httpContext.Items[CachedItemKey] = storedToken;
                return storedToken;
            }

            if (authResult.Properties?.GetTokenValue("id_token") is string idToken)
            {
                var exchangedToken = await tokenExchangeService.ExchangeAsync(idToken, cancellationToken);
                if (exchangedToken is not null)
                {
                    StoreApiToken(authResult.Properties, exchangedToken);
                    await httpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        authResult.Principal,
                        authResult.Properties);
                    var tokenInfo = new WebConsoleApiTokenInfo(exchangedToken.AccessToken, exchangedToken.ExpiresAt);
                    httpContext.Items[CachedItemKey] = tokenInfo;
                    return tokenInfo;
                }
            }
        }

        return null;
    }

    private WebConsoleApiTokenInfo? TryGetStoredApiToken(AuthenticationProperties properties)
    {
        var token = properties.GetTokenValue(ApiTokenName);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var expiresAtValue = properties.GetTokenValue(ApiTokenExpiresAtName);
        if (!DateTimeOffset.TryParse(
                expiresAtValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var expiresAt) ||
            expiresAt <= DateTimeOffset.UtcNow.Add(ExpirationSkew))
        {
            return null;
        }

        return new WebConsoleApiTokenInfo(token, expiresAt);
    }

    private static void StoreApiToken(AuthenticationProperties properties, WebConsoleSessionAccessToken token)
    {
        var tokens = properties.GetTokens()
            .Where(static entry =>
                !string.Equals(entry.Name, ApiTokenName, StringComparison.Ordinal) &&
                !string.Equals(entry.Name, ApiTokenExpiresAtName, StringComparison.Ordinal))
            .ToList();

        tokens.Add(new AuthenticationToken
        {
            Name = ApiTokenName,
            Value = token.AccessToken
        });
        tokens.Add(new AuthenticationToken
        {
            Name = ApiTokenExpiresAtName,
            Value = token.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
        });

        properties.StoreTokens(tokens);
    }
}

internal sealed record WebConsoleApiTokenInfo(string AccessToken, DateTimeOffset? ExpiresAt);
