namespace Makana.Api.Security;

internal static class PulumiAccessTokenTypes
{
    public const string GenericAccessToken = "urn:ietf:params:oauth:token-type:access_token";
    public const string PersonalAccessToken = "urn:pulumi:token-type:access_token:personal";
    public const string OrganizationAccessToken = "urn:pulumi:token-type:access_token:organization";
    public const string TeamAccessToken = "urn:pulumi:token-type:access_token:team";

    public const string Personal = "personal";
    public const string Organization = "organization";
    public const string Team = "team";

    public const string PrincipalUser = "user";
    public const string PrincipalOrganization = "organization";
    public const string PrincipalTeam = "team";

    public const string BearerTokenType = "Bearer";
}
