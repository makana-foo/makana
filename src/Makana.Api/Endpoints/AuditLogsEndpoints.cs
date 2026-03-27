namespace Makana.Api.Endpoints;

internal static class AuditLogsEndpoints
{
    public static IEndpointRouteBuilder MapAuditLogsEndpoints(this IEndpointRouteBuilder app)
    {
        var auditLogs = app.MapGroup("/api/orgs/{org}")
            .RequireAuthorization("OrgRouteAccess");

        auditLogs.MapMethods("/auditlogs", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        auditLogs.MapMethods("/auditlogs/export", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
