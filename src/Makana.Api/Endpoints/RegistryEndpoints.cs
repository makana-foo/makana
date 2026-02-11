using System.Text.Json;
using Makana.Api.Contracts;
using Makana.Api.Storage;

namespace Makana.Api.Endpoints;

internal static class RegistryEndpoints
{
    public static IEndpointRouteBuilder MapRegistryEndpoints(this IEndpointRouteBuilder app, BackendStore store)
    {
        app.MapGet("/api/orgs/{org}/policygroups", () =>
            Results.Ok(new ListPolicyGroupsResponse(Array.Empty<PolicyGroupSummary>())));

        app.MapPatch("/api/orgs/{org}/policygroups/{policyGroup}", () => Results.Ok());

        app.MapGet("/api/orgs/{org}/policypacks", () =>
            Results.Ok(new ListPolicyPacksResponse(Array.Empty<PolicyPackWithVersions>())));

        app.MapPost("/api/orgs/{org}/policypacks", (HttpContext context) =>
        {
            var operationId = Guid.NewGuid().ToString("N");
            var uploadUri = $"{EndpointHelpers.GetBaseUrl(context.Request)}/api/uploads/policypacks/{operationId}/archive";
            return Results.Ok(new CreatePolicyPackResponse(1, uploadUri, null));
        });

        app.MapPost("/api/orgs/{org}/policypacks/{policyPack}/versions/{version}/complete", () => Results.Ok());

        app.MapGet("/api/orgs/{org}/policypacks/{policyPack}/versions/{version}/schema", () =>
            Results.Ok(new PolicyPackConfigSchemaResponse(null)));

        app.MapGet("/api/orgs/{org}/search/resources", () =>
            Results.Ok(new ResourceSearchResponse(0, JsonDocument.Parse("[]").RootElement, null, null, null)));

        app.MapGet("/api/orgs/{org}/search/resources/parse", (HttpRequest request) =>
        {
            var query = request.Query["query"].ToString();
            return Results.Ok(new PulumiQueryResponse(query));
        });

        app.MapGet("/api/orgs/{org}/templates", () =>
        {
            return Results.Ok(new ListOrgTemplatesResponse(new Dictionary<string, PulumiTemplateRemote[]>(), false, false, false));
        });

        app.MapGet("/api/orgs/{org}/template/download", () =>
        {
            return Results.NotFound();
        });

        app.MapGet("/api/preview/registry/packages", () =>
            Results.Ok(new ListPackagesResponse(Array.Empty<PackageMetadata>(), null)));

        app.MapGet("/api/preview/registry/packages/{source}/{publisher}/{name}/versions/{version}",
            (string source, string publisher, string name, string version, HttpContext context) =>
            {
                var baseUrl = EndpointHelpers.GetBaseUrl(context.Request);
                var readmeUrl = $"{baseUrl}/api/preview/registry/packages/{source}/{publisher}/{name}/versions/{version}/readme";
                var schemaUrl = $"{baseUrl}/api/preview/registry/packages/{source}/{publisher}/{name}/versions/{version}/schema";
                var metadata = new PackageMetadata(
                    name,
                    publisher,
                    source,
                    version,
                    null,
                    null,
                    null,
                    null,
                    null,
                    false,
                    null,
                    "ga",
                    readmeUrl,
                    schemaUrl,
                    null,
                    DateTimeOffset.UtcNow.ToString("O"),
                    "public",
                    null
                );
                return Results.Ok(metadata);
            });

        app.MapPost("/api/preview/registry/packages/{source}/{publisher}/{name}/versions",
            async (HttpContext context, string source, string publisher, string name, StartPackagePublishRequest request) =>
            {
                var operationId = Guid.NewGuid().ToString("N");
                await store.CreateRegistryOperationAsync(
                    operationId,
                    "package",
                    JsonSerializer.Serialize(request, RegistryEndpointsJsonContext.Default.StartPackagePublishRequest));
                var baseUrl = EndpointHelpers.GetBaseUrl(context.Request);
                var uploadUrls = new PackageUploadUrls(
                    $"{baseUrl}/api/uploads/packages/{operationId}/schema",
                    $"{baseUrl}/api/uploads/packages/{operationId}/index",
                    $"{baseUrl}/api/uploads/packages/{operationId}/installation"
                );
                return Results.Ok(new StartPackagePublishResponse(operationId, uploadUrls, null));
            });

        app.MapPost("/api/preview/registry/packages/{source}/{publisher}/{name}/versions/{version}/complete",
            async (CompletePackagePublishRequest request) =>
            {
                await store.CompleteRegistryOperationAsync(request.OperationId);
                return Results.Ok();
            });

        app.MapDelete("/api/registry/packages/{source}/{publisher}/{name}/versions/{version}", () => Results.Ok());

        app.MapGet("/api/registry/templates", () =>
            Results.Ok(new ListTemplatesResponse(Array.Empty<TemplateMetadata>(), null)));

        app.MapGet("/api/registry/templates/{source}/{publisher}/{name}/versions/{version}",
            (string source, string publisher, string name, string version, HttpContext context) =>
            {
                var baseUrl = EndpointHelpers.GetBaseUrl(context.Request);
                var downloadUrl = $"{baseUrl}/api/registry/templates/{source}/{publisher}/{name}/versions/{version}/download";
                var readmeUrl = $"{baseUrl}/api/registry/templates/{source}/{publisher}/{name}/versions/{version}/readme";
                var metadata = new TemplateMetadata(
                    name,
                    publisher,
                    source,
                    name,
                    null,
                    "unknown",
                    readmeUrl,
                    downloadUrl,
                    null,
                    "public",
                    DateTimeOffset.UtcNow.ToString("O"),
                    null
                );
                return Results.Ok(metadata);
            });

        app.MapPost("/api/registry/templates/{source}/{publisher}/{name}/versions",
            async (HttpContext context, StartTemplatePublishRequest request) =>
            {
                var operationId = Guid.NewGuid().ToString("N");
                await store.CreateRegistryOperationAsync(
                    operationId,
                    "template",
                    JsonSerializer.Serialize(request, RegistryEndpointsJsonContext.Default.StartTemplatePublishRequest));
                var baseUrl = EndpointHelpers.GetBaseUrl(context.Request);
                var uploadUrls = new TemplateUploadUrls($"{baseUrl}/api/uploads/templates/{operationId}/archive");
                return Results.Ok(new StartTemplatePublishResponse(operationId, uploadUrls));
            });

        app.MapPost("/api/registry/templates/{source}/{publisher}/{name}/versions/{version}/complete",
            async (PublishTemplateVersionCompleteRequest request) =>
            {
                await store.CompleteRegistryOperationAsync(request.OperationId);
                return Results.Ok();
            });

        return app;
    }
}
