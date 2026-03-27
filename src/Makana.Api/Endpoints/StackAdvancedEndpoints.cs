using Makana.Api.Contracts;
using Makana.Api.Storage;
using Makana.Api.Updates;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Makana.Api.Endpoints;

internal static class StackAdvancedEndpoints
{
    public static IEndpointRouteBuilder MapStackAdvancedEndpoints(this IEndpointRouteBuilder app)
    {
        var stackGroup = app.MapGroup(StackRouteConventions.ApiStackRoute)
            .RequireAuthorization("OrgRouteAccess");

        stackGroup.MapMethods("/config", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackGroup.MapMethods("/tags/{tagName}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackGroup.MapGet("/config", HandleGetConfigAsync);
        stackGroup.MapMethods("/downstreamreferences", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackGroup.MapMethods("/metadata", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackGroup.MapGet("/resources/count", HandleGetResourceCountAsync);
        stackGroup.MapGet("/resources/latest", HandleGetLatestResourcesAsync);
        stackGroup.MapGet("/resources/{version:int}", HandleGetResourcesByVersionAsync);
        stackGroup.MapMethods("/updates/latest/previews", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackGroup.MapMethods("/tags", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackGroup.MapMethods("/transfer", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }

    private static Results<Ok<StackConfig>, NotFound> HandleGetConfigAsync(
        string org,
        string project,
        string stack,
        StackCatalogQueryRepository stackQueries)
    {
        var ids = stackQueries.GetStackIds(org, project, stack);
        if (ids is null)
        {
            return TypedResults.NotFound();
        }

        var config = stackQueries.GetStackConfig(org, project, stack);
        return TypedResults.Ok(new StackConfig(config));
    }

    private static async Task<Results<Ok<StackResourceCountResponse>, NotFound>> HandleGetResourceCountAsync(
        string org,
        string project,
        string stack,
        VersionedUpdatePageMaterializer versionedUpdatePageMaterializer,
        StackCatalogQueryRepository stackQueries,
        CancellationToken cancellationToken)
    {
        var stackId = ResolveStackId(stackQueries, org, project, stack);
        if (stackId is null)
        {
            return TypedResults.NotFound();
        }

        var response = await versionedUpdatePageMaterializer.BuildResourceCountAsync(stackId, cancellationToken);
        return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<StackResourcesResponse>, NotFound>> HandleGetLatestResourcesAsync(
        string org,
        string project,
        string stack,
        VersionedUpdatePageMaterializer versionedUpdatePageMaterializer,
        StackCatalogQueryRepository stackQueries,
        CancellationToken cancellationToken)
    {
        var stackId = ResolveStackId(stackQueries, org, project, stack);
        if (stackId is null)
        {
            return TypedResults.NotFound();
        }

        var response = await versionedUpdatePageMaterializer.BuildLatestResourcesAsync(stackId, cancellationToken);
        return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<StackResourcesResponse>, NotFound>> HandleGetResourcesByVersionAsync(
        string org,
        string project,
        string stack,
        int version,
        VersionedUpdatePageMaterializer versionedUpdatePageMaterializer,
        StackCatalogQueryRepository stackQueries,
        CancellationToken cancellationToken)
    {
        var stackId = ResolveStackId(stackQueries, org, project, stack);
        if (stackId is null)
        {
            return TypedResults.NotFound();
        }

        var response = await versionedUpdatePageMaterializer.BuildResourcesForVersionAsync(stackId, version, cancellationToken);
        return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
    }

    private static string? ResolveStackId(StackCatalogQueryRepository stackQueries, string org, string project, string stack)
    {
        var ids = stackQueries.GetStackIds(org, project, stack);
        return ids?.StackId;
    }
}
