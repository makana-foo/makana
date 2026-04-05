using Aspire.Hosting.ApplicationModel;

namespace Makana.AppHost.Oidc;

internal static class OidcParameterResourceBuilderExtensions
{
    public static OidcParameterResources AddOidcParameters(this IDistributedApplicationBuilder builder)
    {
        const string providerName = "oidc-provider";
        const string modeName = "oidc-mode";
        const string authorityName = "oidc-authority";
        const string upstreamAuthorityName = "upstream-oidc-authority";
        const string clientIdName = "oidc-client-id";
        const string clientSecretName = "oidc-client-secret";
        var configuredMode = builder.Configuration[$"Parameters:{modeName}"];
        var configuredProvider = builder.Configuration[$"Parameters:{providerName}"];
        var usesBuiltInOidc = !string.Equals(configuredMode, "external", StringComparison.OrdinalIgnoreCase)
            && configuredProvider is "pocketid" or "dex";
        var hasClientSecret = !string.IsNullOrWhiteSpace(builder.Configuration[$"Parameters:{clientSecretName}"]);

        return new OidcParameterResources(
            builder.AddParameter(providerName),
            builder.AddParameter(modeName),
            usesBuiltInOidc ? null : builder.AddParameter(authorityName),
            usesBuiltInOidc ? null : builder.AddParameter(upstreamAuthorityName),
            builder.AddParameter(clientIdName),
            hasClientSecret ? builder.AddParameter(clientSecretName, secret: true) : null);
    }
}

internal sealed record OidcParameterResources(
    IResourceBuilder<ParameterResource> Provider,
    IResourceBuilder<ParameterResource> Mode,
    IResourceBuilder<ParameterResource>? Authority,
    IResourceBuilder<ParameterResource>? UpstreamAuthority,
    IResourceBuilder<ParameterResource> ClientId,
    IResourceBuilder<ParameterResource>? ClientSecret)
{
    public ReferenceExpression MetadataAddress => ReferenceExpression.Create($"{GetRequiredAuthority()}/.well-known/openid-configuration");

    public IResourceBuilder<ParameterResource> GetRequiredAuthority()
        => Authority ?? throw new InvalidOperationException("OIDC authority parameter is only available for externally configured OIDC.");

    public IResourceBuilder<ParameterResource> GetRequiredUpstreamAuthority()
        => UpstreamAuthority ?? throw new InvalidOperationException("OIDC upstream authority parameter is only available for externally configured OIDC.");

    public async Task<bool> UsesBuiltInOidcAsync(CancellationToken cancellationToken = default)
    {
        var mode = await Mode.Resource.GetValueAsync(cancellationToken);
        var provider = await Provider.Resource.GetValueAsync(cancellationToken);
        return mode is not "external" && provider is "pocketid" or "dex";
    }

    public async Task<bool> UsesDexAsync(CancellationToken cancellationToken = default)
    {
        var mode = await Mode.Resource.GetValueAsync(cancellationToken);
        var provider = await Provider.Resource.GetValueAsync(cancellationToken);
        return mode is not "external" && provider is "dex";
    }
}
