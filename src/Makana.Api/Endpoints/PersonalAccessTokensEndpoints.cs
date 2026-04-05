using System.Security.Cryptography;
using Makana.Api.Contracts;
using Makana.Api.Security;
using Makana.Api.Storage;

namespace Makana.Api.Endpoints;

internal static class PersonalAccessTokensEndpoints
{
    public static IEndpointRouteBuilder MapPersonalAccessTokensEndpoints(this IEndpointRouteBuilder app)
    {
        var orgTokens = app.MapGroup("/api/orgs/{org}/tokens")
            .RequireAuthorization("OrgAdminRouteAccess");

        orgTokens.MapGet(string.Empty, (string org, IdentityRepository identityRepository) =>
        {
            var tokens = identityRepository.ListOrganizationTokens(org)
                .Select(token => new OrganizationTokenSummaryResponse(
                    token.TokenId,
                    token.DisplayName,
                    token.Description,
                    token.CreatedByUserId,
                    token.IssuedAt.ToString("O"),
                    token.ExpiresAt?.ToString("O"),
                    token.Revoked))
                .ToArray();
            return Results.Ok(new OrganizationTokensResponse(tokens));
        });

        orgTokens.MapPost(string.Empty, (
            string org,
            CreateOrganizationTokenRequest request,
            AuthenticatedApiContextAccessor authenticatedApiContextAccessor,
            IdentityRepository identityRepository) =>
        {
            var caller = authenticatedApiContextAccessor.GetCurrent();
            if (string.IsNullOrWhiteSpace(caller.UserId))
            {
                return Results.Unauthorized();
            }

            var organization = identityRepository.GetOrganizationByLogin(org);
            if (organization is null)
            {
                return Results.NotFound();
            }

            var issuedAt = DateTimeOffset.UtcNow;
            var expiresAt = request.ExpiresInDays is > 0
                ? issuedAt.AddDays(request.ExpiresInDays.Value)
                : (DateTimeOffset?)null;
            var token = CreateOpaqueAccessToken();
            identityRepository.CreateAccessToken(
                token,
                PulumiAccessTokenTypes.Organization,
                PulumiAccessTokenTypes.PrincipalOrganization,
                organization.Id,
                caller.UserId,
                organization.Id,
                teamId: null,
                request.Name,
                request.Description,
                issuedAt,
                expiresAt,
                issuedVia: "organization-pat",
                linkedIdentityId: null,
                createdByUserId: caller.UserId,
                scopeAdmin: true);

            var tokenRecord = identityRepository.GetApiToken(token)!;
            return Results.Ok(new CreateOrganizationTokenResponse(
                tokenRecord.TokenId,
                token,
                request.Name,
                expiresAt?.ToString("O")));
        });

        orgTokens.MapDelete("/{tokenId}", (string org, string tokenId, IdentityRepository identityRepository) =>
        {
            return identityRepository.RevokeOrganizationToken(org, tokenId)
                ? Results.NoContent()
                : Results.NotFound();
        });

        var userTokens = app.MapGroup("/api/user/tokens")
            .RequireAuthorization();
        userTokens.MapMethods("/{tokenId}", ["DELETE"], static () => Results.StatusCode(StatusCodes.Status501NotImplemented));
        userTokens.MapMethods("", ["GET"], static () => Results.StatusCode(StatusCodes.Status501NotImplemented));
        userTokens.MapMethods("", ["POST"], static () => Results.StatusCode(StatusCodes.Status501NotImplemented));

        return app;
    }

    private static string CreateOpaqueAccessToken()
        => "pul-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
}
