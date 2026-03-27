namespace Makana.Api.Security;

internal static class PulumiAuthenticationDefaults
{
    public const string Scheme = "Pulumi";
    public const string TokenScheme = "PulumiToken";
    public const string UpdateScheme = "PulumiUpdateToken";
    public const string AuthTokenClaim = "makana:auth_token";
    public const string UpdateTokenClaim = "makana:update_token";
    public const string UserIdClaim = "makana:user_id";
    public const string LoginClaim = "preferred_username";
    public const string OrgIdClaim = "makana:org_id";
    public const string TeamIdClaim = "makana:team_id";
    public const string TokenTypeClaim = "makana:token_type";
    public const string PrincipalTypeClaim = "makana:principal_type";
    public const string PrincipalIdClaim = "makana:principal_id";
    public const string ScopeAdminClaim = "makana:scope_admin";
}
