using System.Text;

namespace Aspire.Hosting.ApplicationModel;

internal sealed class DexResource(string name) : ContainerResource(name)
{
    internal const string ConfigurationFileTargetPath = "/etc/dex/config.yaml";

    internal string ConfigurationFileHostPath { get; } = Path.Combine(
        Path.GetTempPath(),
        "Makana",
        "Aspire",
        "Dex",
        name,
        "config.yaml");

    internal bool StaticClientIsPublic { get; set; }

    internal string RenderConfiguration(DexConfiguration configuration)
    {
        StringBuilder builder = new();
        builder.AppendLine($"issuer: {YamlString(configuration.Issuer)}");
        builder.AppendLine("storage:");
        builder.AppendLine("  type: sqlite3");
        builder.AppendLine("  config:");
        builder.AppendLine($"    file: {YamlString(configuration.StoragePath)}");
        builder.AppendLine("web:");
        builder.AppendLine("  http: 0.0.0.0:5556");
        builder.AppendLine("oauth2:");
        builder.AppendLine("  skipApprovalScreen: true");
        builder.AppendLine("alwaysShowLoginScreen: false");
        builder.AppendLine("enablePasswordDB: false");
        builder.AppendLine("staticClients:");
        builder.AppendLine($"  - id: {YamlString(configuration.StaticClientId)}");
        builder.AppendLine("    name: 'Makana Web Console'");
        builder.AppendLine($"    public: {YamlBool(StaticClientIsPublic)}");

        if (!StaticClientIsPublic)
        {
            builder.AppendLine($"    secret: {YamlString(configuration.StaticClientSecret)}");
        }

        builder.AppendLine("    redirectURIs:");
        builder.AppendLine($"      - {YamlString(configuration.StaticClientRedirectUri)}");
        builder.AppendLine("connectors:");
        builder.AppendLine("  - type: oidc");
        builder.AppendLine($"    id: {YamlString(configuration.ConnectorId)}");
        builder.AppendLine($"    name: {YamlString(configuration.ConnectorName)}");
        builder.AppendLine("    config:");
        builder.AppendLine($"      issuer: {YamlString(configuration.UpstreamIssuer)}");
        if (!string.IsNullOrWhiteSpace(configuration.UpstreamIssuerAlias))
        {
            builder.AppendLine($"      issuerAlias: {YamlString(configuration.UpstreamIssuerAlias)}");
        }
        builder.AppendLine($"      clientID: {YamlString(configuration.UpstreamClientId)}");
        builder.AppendLine($"      clientSecret: {YamlString(configuration.UpstreamClientSecret)}");
        builder.AppendLine($"      redirectURI: {YamlString(configuration.UpstreamRedirectUri)}");
        if (!string.IsNullOrWhiteSpace(configuration.UpstreamPkceChallenge))
        {
            builder.AppendLine($"      pkceChallenge: {YamlString(configuration.UpstreamPkceChallenge)}");
        }
        builder.AppendLine("      getUserInfo: false");
        builder.AppendLine("      scopes:");
        builder.AppendLine("        - openid");
        builder.AppendLine("        - profile");
        builder.AppendLine("        - email");
        builder.AppendLine("      providerDiscoveryOverrides:");
        builder.AppendLine($"        authURL: {YamlString(configuration.UpstreamAuthUrl)}");
        builder.AppendLine($"        tokenURL: {YamlString(configuration.UpstreamTokenUrl)}");
        builder.AppendLine($"        jwksURL: {YamlString(configuration.UpstreamJwksUrl)}");

        return builder.ToString();
    }

    private static string YamlBool(bool value)
        => value ? "true" : "false";

    private static string YamlString(string value)
        => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}

internal sealed record DexConfiguration(
    string Issuer,
    string StoragePath,
    string StaticClientId,
    string StaticClientSecret,
    string StaticClientRedirectUri,
    string ConnectorId,
    string ConnectorName,
    string UpstreamIssuer,
    string UpstreamIssuerAlias,
    string UpstreamClientId,
    string UpstreamClientSecret,
    string UpstreamPkceChallenge,
    string UpstreamRedirectUri,
    string UpstreamAuthUrl,
    string UpstreamTokenUrl,
    string UpstreamJwksUrl);
