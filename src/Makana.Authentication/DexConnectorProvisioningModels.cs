namespace Makana.Authentication;

public sealed record OrganizationOidcIdentityProviderConfiguration(
    string Issuer,
    string ClientId,
    string ClientSecret);

public sealed record OrganizationSamlIdentityProviderConfiguration(
    string SsoUrl,
    string CaCertificatePem,
    string UsernameAttribute,
    string EmailAttribute,
    string? GroupsAttribute,
    string? EntityIssuer,
    string? SsoIssuer,
    string? GroupsDelimiter,
    string? NameIdPolicyFormat);

public sealed record OrganizationSsoProvisioningRequest(
    string OrganizationLogin,
    string Protocol,
    string DisplayName,
    string RedirectUri,
    string? ExistingConnectionId,
    OrganizationOidcIdentityProviderConfiguration? Oidc,
    OrganizationSamlIdentityProviderConfiguration? Saml);

public sealed record OrganizationSsoProvisioningResult(string ConnectionId);
