using Aspire.Hosting.ApplicationModel;

namespace Makana.AppHost;

public static class ProjectResourceBuilderExtensions
{
    public static ReferenceExpression GetEndpointUrlReference<TSource>(
        this IResourceBuilder<TSource> source,
        string sourceEndpointName = "https")
            where TSource : IResourceWithEndpoints
    {
        return source.GetEndpointUrlReference(sourceEndpointName, contextNetworkId: null);
    }

    public static ReferenceExpression GetEndpointUrlReference<TSource>(
        this IResourceBuilder<TSource> source,
        string host,
        string sourceEndpointName = "https")
            where TSource : IResourceWithEndpoints
    {
        return source.GetEndpointUrlReference(sourceEndpointName, contextNetworkId: null, host);
    }

    public static ReferenceExpression GetEndpointUrlReference<TSource>(
        this IResourceBuilder<TSource> source,
        string sourceEndpointName,
        NetworkIdentifier? contextNetworkId)
            where TSource : IResourceWithEndpoints
    {
        var sourceEndpoint = contextNetworkId is null
            ? source.GetEndpoint(sourceEndpointName)
            : source.GetEndpoint(sourceEndpointName, contextNetworkId);

        return CreateUrlReference(
            sourceEndpoint.Property(EndpointProperty.Scheme),
            sourceEndpoint.Property(EndpointProperty.Host),
            sourceEndpoint.Property(EndpointProperty.Port));
    }

    public static ReferenceExpression GetEndpointUrlReference<TSource>(
        this IResourceBuilder<TSource> source,
        string sourceEndpointName,
        NetworkIdentifier? contextNetworkId,
        string host)
            where TSource : IResourceWithEndpoints
    {
        var sourceEndpoint = contextNetworkId is null
            ? source.GetEndpoint(sourceEndpointName)
            : source.GetEndpoint(sourceEndpointName, contextNetworkId);

        return CreateUrlReference(
            sourceEndpoint.Property(EndpointProperty.Scheme),
            host,
            sourceEndpoint.Property(EndpointProperty.Port));
    }

    public static IResourceBuilder<TTarget> WithEnvironmentUrlFromEndpoint<TSource, TTarget>(
        this IResourceBuilder<TTarget> target,
        string environmentName,
        IResourceBuilder<TSource> source,
        string sourceEndpointName = "https")
            where TSource : IResourceWithEndpoints
            where TTarget : IResourceWithEnvironment
    {
        target.WithEnvironment(environmentName, source.GetEndpointUrlReference(sourceEndpointName));

        return target;
    }

    public static IResourceBuilder<TTarget> WithEnvironmentUrlFromEndpoint<TSource, TTarget>(
        this IResourceBuilder<TTarget> target,
        string environmentName,
        IResourceBuilder<TSource> source,
        NetworkIdentifier? contextNetworkId,
        string host,
        string sourceEndpointName)
            where TSource : IResourceWithEndpoints
            where TTarget : IResourceWithEnvironment
    {
        target.WithEnvironment(
            environmentName,
            source.GetEndpointUrlReference(sourceEndpointName, contextNetworkId, host));

        return target;
    }

    public static IResourceBuilder<TTarget> WithEnvironmentUrlFromEndpoint<TSource, TTarget>(
        this IResourceBuilder<TTarget> target,
        string environmentName,
        IResourceBuilder<TSource> source,
        string host,
        string sourceEndpointName)
            where TSource : IResourceWithEndpoints
            where TTarget : IResourceWithEnvironment
    {
        var sourceEndpoint = source.GetEndpoint(sourceEndpointName);

        target.WithEnvironment(
            environmentName,
            CreateUrlReference(
                sourceEndpoint.Property(EndpointProperty.Scheme),
                host,
                sourceEndpoint.Property(EndpointProperty.Port)));

        return target;
    }

    public static IResourceBuilder<TTarget> WithDashboardUrlFromEndpoint<TSource, TTarget>(
        this IResourceBuilder<TTarget> target,
        IResourceBuilder<TSource> source,
        NetworkIdentifier? contextNetworkId,
        string host,
        string displayText,
        string sourceEndpointName = "https")
            where TSource : IResourceWithEndpoints
            where TTarget : IResource
    {
        target.WithUrl(
            source.GetEndpointUrlReference(sourceEndpointName, contextNetworkId, host),
            displayText);

        return target;
    }

    public static IResourceBuilder<TTarget> WithBrowserOriginsFromEndpoint<TSource, TTarget>(
        this IResourceBuilder<TTarget> target,
        IResourceBuilder<TSource> source,
        NetworkIdentifier? contextNetworkId,
        string sourceEndpointName = "https",
        params ReadOnlySpan<string> configuredHosts)
            where TSource : IResourceWithEndpoints
            where TTarget : IResourceWithEnvironment
    {
        if (configuredHosts.Length == 0)
        {
            target.WithEnvironment(
                "Cors__AllowedOrigins__0",
                source.GetEndpointUrlReference(sourceEndpointName, contextNetworkId));
            return target;
        }

        for (var i = 0; i < configuredHosts.Length; i++)
        {
            target.WithEnvironment(
                $"Cors__AllowedOrigins__{i}",
                source.GetEndpointUrlReference(sourceEndpointName, contextNetworkId, configuredHosts[i]));
        }

        return target;
    }

    public static IResourceBuilder<TTarget> WithBrowserOriginsFromEndpoint<TSource, TTarget>(
        this IResourceBuilder<TTarget> target,
        IResourceBuilder<TSource> source,
        string sourceEndpointName = "https",
        params ReadOnlySpan<string> configuredHosts)
            where TSource : IResourceWithEndpoints
            where TTarget : IResourceWithEnvironment
    {
        if (configuredHosts.Length == 0)
        {
            target.WithEnvironment(
                "Cors__AllowedOrigins__0",
                source.GetEndpointUrlReference(sourceEndpointName));
            return target;
        }

        var sourceEndpoint = source.GetEndpoint(sourceEndpointName);

        for (var i = 0; i < configuredHosts.Length; i++)
        {
            target.WithEnvironment(
                $"Cors__AllowedOrigins__{i}",
                CreateUrlReference(
                    sourceEndpoint.Property(EndpointProperty.Scheme),
                    configuredHosts[i],
                    sourceEndpoint.Property(EndpointProperty.Port)));
        }

        return target;
    }

    public static IResourceBuilder<TTarget> WithBrowserOrigins<TTarget>(
        this IResourceBuilder<TTarget> target,
        string scheme,
        int port,
        params ReadOnlySpan<string> configuredHosts)
            where TTarget : IResourceWithEnvironment
    {
        if (configuredHosts.Length == 0)
        {
            return target;
        }

        for (var i = 0; i < configuredHosts.Length; i++)
        {
            target.WithEnvironment(
                $"Cors__AllowedOrigins__{i}",
                CreateUrl(scheme, configuredHosts[i], port));
        }

        return target;
    }

    private static ReferenceExpression CreateUrlReference(
        EndpointReferenceExpression scheme,
        EndpointReferenceExpression host,
        EndpointReferenceExpression port)
    {
        var schemeText = scheme.ToString() ?? string.Empty;
        var portText = port.ToString() ?? string.Empty;

        return schemeText switch
        {
            "https" when portText == "443" => ReferenceExpression.Create($"{scheme}://{host}"),
            "http" when portText == "80" => ReferenceExpression.Create($"{scheme}://{host}"),
            _ => ReferenceExpression.Create($"{scheme}://{host}:{port}")
        };
    }

    private static ReferenceExpression CreateUrlReference(
        EndpointReferenceExpression scheme,
        string host,
        EndpointReferenceExpression port)
    {
        var schemeText = scheme.ToString() ?? string.Empty;
        var portText = port.ToString() ?? string.Empty;

        return schemeText switch
        {
            "https" when portText == "443" => ReferenceExpression.Create($"{scheme}://{host}"),
            "http" when portText == "80" => ReferenceExpression.Create($"{scheme}://{host}"),
            _ => ReferenceExpression.Create($"{scheme}://{host}:{port}")
        };
    }

    private static string CreateUrl(string scheme, string host, int port)
    {
        return scheme switch
        {
            "https" when port == 443 => $"https://{host}",
            "http" when port == 80 => $"http://{host}",
            _ => $"{scheme}://{host}:{port}"
        };
    }

}
