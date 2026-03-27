using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

namespace Makana.AppHost;

public static class ProjectResourceBuilderExtensions
{
    public static IResourceBuilder<TTarget> WithEnvironmentUrlFromEndpoint<TSource, TTarget>(
        this IResourceBuilder<TTarget> target,
        string environmentName,
        IResourceBuilder<TSource> source,
        string sourceEndpointName = "https")
            where TSource : IResourceWithEndpoints
            where TTarget : IResourceWithEnvironment
    {
        var sourceEndpoint = source.GetEndpoint(sourceEndpointName);

        target.WithEnvironment(
            environmentName,
            CreateUrlReference(
                sourceEndpoint.Property(EndpointProperty.Scheme),
                sourceEndpoint.Property(EndpointProperty.Host),
                sourceEndpoint.Property(EndpointProperty.Port)));

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
            return target;
        }

        var sourceEndpoint = source.GetEndpoint(sourceEndpointName);
        var scheme = sourceEndpoint.IsHttps ? "https" : "http";

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

}
