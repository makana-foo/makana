namespace Makana.Api.Security;

internal static class PulumiAuthenticationDefaults
{
    public const string Scheme = "PulumiToken";
    public const string CombinedScheme = "ApiAuth";
    public const string AuthKindClaim = "makana:auth_kind";
    public const string AuthTokenClaim = "makana:auth_token";
    public const string UserIdClaim = "makana:user_id";
    public const string OrgIdClaim = "makana:org_id";
}
