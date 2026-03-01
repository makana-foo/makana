using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Makana.Api.Security;

internal sealed class OidcUserInfoClient(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    ILogger<OidcUserInfoClient> logger)
{
    private const string UserInfoEndpointCacheKey = "makana:oidc:userinfo-endpoint";

    private readonly string _authority = configuration["OIDC_AUTHORITY"]
        ?? throw new InvalidOperationException("OIDC_AUTHORITY is required for OIDC userinfo resolution.");
    private readonly string? _metadataAddress = configuration["OIDC_METADATA_ADDRESS"];

    public async Task<ClaimsPrincipal?> TryEnrichPrincipalAsync(
        HttpContext context,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var accessToken = await context.GetTokenAsync(
            PulumiAuthenticationDefaults.OidcApiBearerScheme,
            "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        var endpoint = await GetUserInfoEndpointAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "OIDC userinfo call failed with status {StatusCode}.",
                (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        if (principal.Identity is not ClaimsIdentity baseIdentity)
        {
            return principal;
        }

        var enriched = new ClaimsIdentity(
            baseIdentity.Claims,
            baseIdentity.AuthenticationType,
            baseIdentity.NameClaimType,
            baseIdentity.RoleClaimType);

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (enriched.HasClaim(claim => string.Equals(claim.Type, property.Name, StringComparison.Ordinal)))
            {
                continue;
            }

            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(value))
            {
                enriched.AddClaim(new Claim(property.Name, value));
            }
        }

        return new ClaimsPrincipal(enriched);
    }

    private async Task<string?> GetUserInfoEndpointAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue<string>(UserInfoEndpointCacheKey, out var cachedEndpoint) &&
            !string.IsNullOrWhiteSpace(cachedEndpoint))
        {
            return cachedEndpoint;
        }

        var metadataAddress = !string.IsNullOrWhiteSpace(_metadataAddress)
            ? _metadataAddress
            : $"{_authority.TrimEnd('/')}/.well-known/openid-configuration";

        var manager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = false });
        var configuration = await manager.GetConfigurationAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(configuration.UserInfoEndpoint))
        {
            return null;
        }

        cache.Set(
            UserInfoEndpointCacheKey,
            configuration.UserInfoEndpoint,
            TimeSpan.FromMinutes(15));
        return configuration.UserInfoEndpoint;
    }
}
