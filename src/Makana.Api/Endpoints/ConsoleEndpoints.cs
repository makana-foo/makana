using System.Diagnostics.CodeAnalysis;
using Makana.Api.Contracts;

namespace Makana.Api.Endpoints;

internal static class ConsoleEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapConsoleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/console/orgs/{org}/integrations/github-app", () => Results.Ok(new GitHubAppIntegration(false)));
        return app;
    }
}
