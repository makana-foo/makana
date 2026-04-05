using System.Text.Json.Serialization;

namespace Makana.WebConsole.Models;

public sealed record CreateOrganizationRequest(
    [property: JsonPropertyName("name")] string Name);

public sealed record CreateOrganizationResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("redirectPath")] string RedirectPath);

public sealed record OrganizationMember(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("membershipKind")] string MembershipKind);

public sealed record OrganizationMembersResponse(
    [property: JsonPropertyName("members")] OrganizationMember[] Members);

public sealed record CreateOrganizationInviteRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role);

public sealed record OrganizationInvite(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("createdAt")] string CreatedAt,
    [property: JsonPropertyName("claimed")] bool Claimed,
    [property: JsonPropertyName("revoked")] bool Revoked);

public sealed record OrganizationInvitesResponse(
    [property: JsonPropertyName("invites")] OrganizationInvite[] Invites);

public sealed record UpdateOrganizationMemberRequest(
    [property: JsonPropertyName("role")] string Role);

public sealed record CreateOrganizationTokenRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("expiresInDays")] int? ExpiresInDays);

public sealed record OrganizationTokenSummary(
    [property: JsonPropertyName("tokenId")] string TokenId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("createdByUserId")] string? CreatedByUserId,
    [property: JsonPropertyName("issuedAt")] string IssuedAt,
    [property: JsonPropertyName("expiresAt")] string? ExpiresAt,
    [property: JsonPropertyName("revoked")] bool Revoked);

public sealed record OrganizationTokensResponse(
    [property: JsonPropertyName("tokens")] OrganizationTokenSummary[] Tokens);

public sealed record CreateOrganizationTokenResponse(
    [property: JsonPropertyName("tokenId")] string TokenId,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("expiresAt")] string? ExpiresAt);

public sealed record OrganizationOidcConfigurationRequest(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("clientSecret")] string ClientSecret);

public sealed record OrganizationSamlConfigurationRequest(
    [property: JsonPropertyName("ssoUrl")] string SsoUrl,
    [property: JsonPropertyName("caCertificatePem")] string CaCertificatePem,
    [property: JsonPropertyName("usernameAttribute")] string UsernameAttribute,
    [property: JsonPropertyName("emailAttribute")] string EmailAttribute,
    [property: JsonPropertyName("groupsAttribute")] string? GroupsAttribute,
    [property: JsonPropertyName("entityIssuer")] string? EntityIssuer,
    [property: JsonPropertyName("ssoIssuer")] string? SsoIssuer,
    [property: JsonPropertyName("groupsDelimiter")] string? GroupsDelimiter,
    [property: JsonPropertyName("nameIdPolicyFormat")] string? NameIdPolicyFormat);

public sealed record UpdateOrganizationSsoRequest(
    [property: JsonPropertyName("protocol")] string Protocol,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("domains")] string[]? Domains,
    [property: JsonPropertyName("scimBearerToken")] string? ScimBearerToken,
    [property: JsonPropertyName("oidc")] OrganizationOidcConfigurationRequest? Oidc,
    [property: JsonPropertyName("saml")] OrganizationSamlConfigurationRequest? Saml);

public sealed record OrganizationSsoConfiguration(
    [property: JsonPropertyName("protocol")] string Protocol,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("connectionId")] string? ConnectionId,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("domains")] string[] Domains,
    [property: JsonPropertyName("hasScimBearerToken")] bool HasScimBearerToken,
    [property: JsonPropertyName("scimBearerToken")] string? ScimBearerToken,
    [property: JsonPropertyName("oidc")] OrganizationOidcConfigurationRequest? Oidc,
    [property: JsonPropertyName("saml")] OrganizationSamlConfigurationRequest? Saml);

public sealed record OrganizationLoginDiscovery(
    [property: JsonPropertyName("organization")] string Organization,
    [property: JsonPropertyName("protocol")] string Protocol,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("connectionId")] string? ConnectionId,
    [property: JsonPropertyName("displayName")] string? DisplayName);
