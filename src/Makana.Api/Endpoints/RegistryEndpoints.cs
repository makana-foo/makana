using System.Text.Json;
using Makana.Api.Contracts;
using Makana.Api.Storage;

namespace Makana.Api.Endpoints;

internal static class RegistryEndpoints
{
    public static IEndpointRouteBuilder MapRegistryEndpoints(this IEndpointRouteBuilder app, RegistryRepository registryRepository)
    {
        var orgRegistry = app.MapGroup("/api/orgs/{org}")
            .RequireAuthorization("OrgRouteAccess");

        orgRegistry.MapGet("/policygroups", () =>
            Results.Ok(new ListPolicyGroupsResponse([])));

        orgRegistry.MapPatch("/policygroups/{policyGroup}", () => Results.Ok())
            .RequireAuthorization("ApiToken");

        orgRegistry.MapGet("/policypacks", () =>
            Results.Ok(new ListPolicyPacksResponse([])));

        orgRegistry.MapPost("/policypacks", (HttpContext context, EndpointHelpers endpointHelpers) =>
        {
            var operationId = Guid.NewGuid().ToString("N");
            var uploadUri = $"{endpointHelpers.GetBaseUrl(context.Request)}/api/uploads/policypacks/{operationId}/archive";
            return Results.Ok(new CreatePolicyPackResponse(1, uploadUri, null));
        }).RequireAuthorization("ApiToken");

        orgRegistry.MapPost("/policypacks/{policyPack}/versions/{version}/complete", () => Results.Ok())
            .RequireAuthorization("ApiToken");

        orgRegistry.MapGet("/policypacks/{policyPack}/versions/{version}/schema", () =>
            Results.Ok(new PolicyPackConfigSchemaResponse(null)));

        orgRegistry.MapGet("/search/resources", () =>
            Results.Ok(new ResourceSearchResponse(0, JsonDocument.Parse("[]").RootElement, null, null, null)));

        orgRegistry.MapGet("/search/resources/parse", (HttpRequest request) =>
        {
            var query = request.Query["query"].ToString();
            return Results.Ok(new PulumiQueryResponse(query));
        });

        orgRegistry.MapGet("/templates", () =>
        {
            return Results.Ok(new ListOrgTemplatesResponse([], false, false, false));
        });

        orgRegistry.MapGet("/template/download", () =>
        {
            return Results.NotFound();
        });

        app.MapGet("/api/preview/registry/packages", () =>
            Results.Ok(new ListPackagesResponse([], null)));

        app.MapGet("/api/preview/registry/packages/{source}/{publisher}/{name}/versions/{version}",
            (string source, string publisher, string name, string version, HttpContext context, EndpointHelpers endpointHelpers) =>
            {
                var baseUrl = endpointHelpers.GetBaseUrl(context.Request);
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
            async (HttpContext context, string source, string publisher, string name, StartPackagePublishRequest request, EndpointHelpers endpointHelpers) =>
            {
                var operationId = Guid.NewGuid().ToString("N");
                registryRepository.CreateRegistryOperation(
                    operationId,
                    "package",
                    JsonSerializer.Serialize(request, RegistryEndpointsJsonContext.Default.StartPackagePublishRequest));
                var baseUrl = endpointHelpers.GetBaseUrl(context.Request);
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
                registryRepository.CompleteRegistryOperation(request.OperationId);
                return Results.Ok();
            });

        app.MapDelete("/api/registry/packages/{source}/{publisher}/{name}/versions/{version}", () => Results.Ok());

        app.MapGet("/api/registry/templates", () =>
            Results.Ok(new ListTemplatesResponse([], null)));

        app.MapGet("/api/registry/templates/{source}/{publisher}/{name}/versions/{version}",
            (string source, string publisher, string name, string version, HttpContext context, EndpointHelpers endpointHelpers) =>
            {
                var baseUrl = endpointHelpers.GetBaseUrl(context.Request);
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
            async (HttpContext context, StartTemplatePublishRequest request, EndpointHelpers endpointHelpers) =>
            {
                var operationId = Guid.NewGuid().ToString("N");
                registryRepository.CreateRegistryOperation(
                    operationId,
                    "template",
                    JsonSerializer.Serialize(request, RegistryEndpointsJsonContext.Default.StartTemplatePublishRequest));
                var baseUrl = endpointHelpers.GetBaseUrl(context.Request);
                var uploadUrls = new TemplateUploadUrls($"{baseUrl}/api/uploads/templates/{operationId}/archive");
                return Results.Ok(new StartTemplatePublishResponse(operationId, uploadUrls));
            });

        app.MapPost("/api/registry/templates/{source}/{publisher}/{name}/versions/{version}/complete",
            async (PublishTemplateVersionCompleteRequest request) =>
            {
                registryRepository.CompleteRegistryOperation(request.OperationId);
                return Results.Ok();
            });

        return app;
    }
}
