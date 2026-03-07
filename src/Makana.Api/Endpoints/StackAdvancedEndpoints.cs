using System.Diagnostics.CodeAnalysis;
using Makana.Api.Contracts;
using Makana.Api.Security;
using Makana.Api.Storage;
using Makana.Api.Updates;

namespace Makana.Api.Endpoints;

internal static class StackAdvancedEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapStackAdvancedEndpoints(this IEndpointRouteBuilder app)
    {
        static async Task<(string? NamespaceId, IResult? Failure)> ResolveNamespaceIdForRouteAsync(
            HttpContext context,
            NamespaceRoutingHelpers namespaceRoutingHelpers,
            string org)
        {
            var userId = context.User.FindFirst(PulumiAuthenticationDefaults.UserIdClaim)?.Value
                ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return (null, Results.Unauthorized());
            }

            var tokenOrgId = context.User.FindFirst(PulumiAuthenticationDefaults.OrgIdClaim)?.Value;
            var namespaceId = await namespaceRoutingHelpers.ResolveAccessibleNamespaceIdAsync(
                userId,
                org,
                tokenOrgId);
            if (!string.IsNullOrWhiteSpace(namespaceId))
            {
                return (namespaceId, null);
            }

            return (null, Results.NotFound(new ErrorResponse(404, "Organization not found", null)));
        }

        app.MapMethods("/api/stacks/{org}/{project}/{stack}/config", new[] { "DELETE" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{org}/{project}/{stack}/tags/{tagName}", new[] { "DELETE" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{org}/{project}/{stack}/config", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{org}/{project}/{stack}/downstreamreferences", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{org}/{project}/{stack}/metadata", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{org}/{project}/{stack}/resources/count", new[] { "GET" }, async (HttpContext context, string org, string project, string stack, NamespaceRoutingHelpers namespaceRoutingHelpers, VersionedUpdatePageMaterializer versionedUpdatePageMaterializer, BackendStore store, CancellationToken cancellationToken) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var ids = await store.GetStackIdsByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var response = await versionedUpdatePageMaterializer.BuildResourceCountAsync(ids.Value.StackId, cancellationToken);
            return response is null ? Results.NotFound() : Results.Ok(response);
        }).RequireAuthorization("OrgRouteAccess");
        app.MapMethods("/api/stacks/{org}/{project}/{stack}/resources/latest", new[] { "GET" }, async (HttpContext context, string org, string project, string stack, NamespaceRoutingHelpers namespaceRoutingHelpers, VersionedUpdatePageMaterializer versionedUpdatePageMaterializer, BackendStore store, CancellationToken cancellationToken) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var ids = await store.GetStackIdsByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var response = await versionedUpdatePageMaterializer.BuildLatestResourcesAsync(ids.Value.StackId, cancellationToken);
            return response is null ? Results.NotFound() : Results.Ok(response);
        }).RequireAuthorization("OrgRouteAccess");
        app.MapMethods("/api/stacks/{org}/{project}/{stack}/resources/{version}", new[] { "GET" }, async (HttpContext context, string org, string project, string stack, int version, NamespaceRoutingHelpers namespaceRoutingHelpers, VersionedUpdatePageMaterializer versionedUpdatePageMaterializer, BackendStore store, CancellationToken cancellationToken) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var ids = await store.GetStackIdsByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var response = await versionedUpdatePageMaterializer.BuildResourcesForVersionAsync(ids.Value.StackId, version, cancellationToken);
            return response is null ? Results.NotFound() : Results.Ok(response);
        }).RequireAuthorization("OrgRouteAccess");
        app.MapMethods("/api/stacks/{org}/{project}/{stack}/updates/latest/previews", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{org}/{project}/{stack}/tags", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{org}/{project}/{stack}/transfer", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
