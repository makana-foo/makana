using System.Security.Claims;
using Makana.Api.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Makana.Api.Security;

internal static class JwtBearerConfiguration
{
    public static void ConfigureApiBearer(
        JwtBearerOptions options,
        string authority,
        string? metadataAddress,
        string audience,
        string providerKey)
    {
        ConfigureCommon(
            options,
            authority,
            metadataAddress,
            audience,
            providerKey,
            expectedTokenType: "oauth-access-token");
        options.UseSignalRQueryStringAccessToken("/hubs/stack-updates");
    }

    public static void ConfigureSubjectTokenBearer(
        JwtBearerOptions options,
        string authority,
        string? metadataAddress,
        string audience,
        string providerKey)
    {
        ConfigureCommon(
            options,
            authority,
            metadataAddress,
            audience,
            providerKey,
            expectedTokenType: "id-token");
        options.UseFormSubjectToken("/api/oauth/token");
    }

    private static void ConfigureCommon(
        JwtBearerOptions options,
        string authority,
        string? metadataAddress,
        string audience,
        string providerKey,
        string expectedTokenType)
    {
        options.Authority = authority.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(metadataAddress))
        {
            options.MetadataAddress = metadataAddress;
        }

        options.SaveToken = true;
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                if (context.Principal?.Identity is not ClaimsIdentity identity)
                {
                    return;
                }

                var actualTokenType = identity.FindFirst("type")?.Value;
                if (!string.Equals(actualTokenType, expectedTokenType, StringComparison.Ordinal))
                {
                    context.Fail($"Token type '{actualTokenType ?? "<missing>"}' is not valid for this endpoint.");
                    return;
                }

                if (!identity.HasClaim(c => c.Type == PulumiAuthenticationDefaults.AuthKindClaim))
                {
                    identity.AddClaim(new Claim(PulumiAuthenticationDefaults.AuthKindClaim, "bearer"));
                }

                if (identity.HasClaim(c => c.Type == PulumiAuthenticationDefaults.UserIdClaim))
                {
                    return;
                }

                var canonicalUserId = OidcClaimHelpers.ResolveCanonicalUserId(context.Principal, providerKey);
                var resolvedUserId = canonicalUserId;
                var login = OidcClaimHelpers.ResolveLogin(context.Principal);
                if (!string.IsNullOrWhiteSpace(login))
                {
                    var identityRepository = context.HttpContext.RequestServices.GetRequiredService<IdentityRepository>();
                    var existingUserId = identityRepository.GetUserIdByLogin(login);
                    if (!string.IsNullOrWhiteSpace(existingUserId))
                    {
                        resolvedUserId = existingUserId;
                    }
                }

                if (!string.IsNullOrWhiteSpace(resolvedUserId))
                {
                    identity.AddClaim(new Claim(PulumiAuthenticationDefaults.UserIdClaim, resolvedUserId));
                }
            }
        };
    }
}
