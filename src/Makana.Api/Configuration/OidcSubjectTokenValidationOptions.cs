namespace Makana.Api.Configuration;

internal sealed class OidcSubjectTokenValidationOptions
{
    public string Authority { get; set; } = string.Empty;

    public string MetadataAddress { get; set; } = string.Empty;

    public string SubjectTokenAudience { get; set; } = string.Empty;

    public int MaxAccessTokenLifetimeSeconds { get; set; } = 86400;

    public int ClockSkewSeconds { get; set; } = 300;
}
