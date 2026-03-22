using System.Diagnostics.CodeAnalysis;

namespace Makana.Api.Endpoints;

internal static class AuditLogsEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapAuditLogsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/orgs/{organization}/auditlogs", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/auditlogs/export", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
