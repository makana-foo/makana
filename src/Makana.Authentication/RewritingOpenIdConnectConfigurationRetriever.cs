using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Makana.Authentication;

internal sealed class RewritingOpenIdConnectConfigurationRetriever(
    ResolvedOidcAuthorityOptions options) : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    private readonly OpenIdConnectConfigurationRetriever _inner = new();

    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address,
        IDocumentRetriever retriever,
        CancellationToken cancel)
    {
        var effectiveRetriever = options.BackchannelBaseUri is null
            ? retriever
            : new RewritingDocumentRetriever(retriever, RewriteEndpoint);

        var configuration = await ((IConfigurationRetriever<OpenIdConnectConfiguration>)_inner)
            .GetConfigurationAsync(address, effectiveRetriever, cancel);
        if (options.BackchannelBaseUri is null)
        {
            return configuration;
        }

        configuration.TokenEndpoint = RewriteEndpoint(configuration.TokenEndpoint);
        configuration.UserInfoEndpoint = RewriteEndpoint(configuration.UserInfoEndpoint);
        configuration.JwksUri = RewriteEndpoint(configuration.JwksUri);

        return configuration;
    }

    private string RewriteEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) ||
            options.BackchannelBaseUri is null ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
            !Uri.TryCreate(options.Authority, UriKind.Absolute, out var publicAuthorityUri) ||
            !AuthoritiesMatch(endpointUri, publicAuthorityUri))
        {
            return endpoint;
        }

        var builder = new UriBuilder(options.BackchannelBaseUri)
        {
            Path = CombinePath(options.BackchannelBaseUri.AbsolutePath, endpointUri.AbsolutePath),
            Query = endpointUri.Query.TrimStart('?'),
            Fragment = endpointUri.Fragment.TrimStart('#')
        };

        return builder.Uri.ToString();
    }

    private static bool AuthoritiesMatch(Uri left, Uri right)
        => string.Equals(left.GetLeftPart(UriPartial.Authority), right.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

    private static string CombinePath(string basePath, string absolutePath)
    {
        var normalizedBase = string.IsNullOrWhiteSpace(basePath) ? "/" : basePath.TrimEnd('/');
        var normalizedAbsolute = string.IsNullOrWhiteSpace(absolutePath) ? string.Empty : absolutePath.TrimStart('/');

        return normalizedAbsolute.Length == 0
            ? normalizedBase
            : $"{normalizedBase}/{normalizedAbsolute}";
    }

    private sealed class RewritingDocumentRetriever(
        IDocumentRetriever inner,
        Func<string, string> rewriteEndpoint) : IDocumentRetriever
    {
        public Task<string> GetDocumentAsync(string address, CancellationToken cancel)
            => inner.GetDocumentAsync(rewriteEndpoint(address), cancel);
    }
}
