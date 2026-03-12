using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

namespace Makana.AppHost;

public static class ProjectResourceBuilderExtensions
{
    public static IResourceBuilder<ProjectResource> WithEnvironmentUrlFromEndpoint(
        this IResourceBuilder<ProjectResource> target,
        string environmentName,
        IResourceBuilder<ProjectResource> source,
        string sourceEndpointName = "https")
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

    public static IResourceBuilder<ProjectResource> WithBrowserOriginsFromEndpoint(
        this IResourceBuilder<ProjectResource> target,
        IResourceBuilder<ProjectResource> source,
        string sourceEndpointName = "https",
        params ReadOnlySpan<string> configuredHosts)
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
                $"{scheme}://{configuredHosts[i]}");
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

}
