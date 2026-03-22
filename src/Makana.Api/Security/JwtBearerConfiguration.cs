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
        ConfigureCommon(options, authority, metadataAddress, audience, providerKey);
    }

    public static void ConfigureSubjectTokenBearer(
        JwtBearerOptions options,
        string authority,
        string? metadataAddress,
        string audience,
        string providerKey)
    {
        ConfigureCommon(options, authority, metadataAddress, audience, providerKey);

        options.Events.OnMessageReceived = async context =>
        {
            if (!context.Request.Path.Equals("/api/oauth/token", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!context.Request.HasFormContentType)
            {
                return;
            }

            var form = await context.Request.ReadFormAsync(context.HttpContext.RequestAborted);
            var subjectToken = form["subject_token"].ToString();
            if (!string.IsNullOrWhiteSpace(subjectToken))
            {
                context.Token = subjectToken;
            }
        };
    }

    private static void ConfigureCommon(
        JwtBearerOptions options,
        string authority,
        string? metadataAddress,
        string audience,
        string providerKey)
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
                    var store = context.HttpContext.RequestServices.GetRequiredService<BackendStore>();
                    var existingUserId = await store.GetUserIdByLoginAsync(login);
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
