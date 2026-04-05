using System.Security.Claims;

namespace Makana.Authentication;

public sealed class MakanaOidcTokenValidationOptions
{
    public string AuthorityName { get; set; } = MakanaOidcDefaults.DefaultAuthorityName;

    public List<string> ValidAudiences { get; set; } = [];

    public List<string> ValidIssuers { get; set; } = [];

    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);

    public string NameClaimType { get; set; } = ClaimTypes.Name;
}
