using Makana.WebConsole.Models;
using Makana.WebConsole.Services;

namespace Makana.WebConsole.Endpoints;

internal static class UserPreferenceEndpoints
{
    public static IEndpointRouteBuilder MapUserPreferenceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/user/theme", HandleSetThemeAsync)
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HandleSetThemeAsync(
        UpdateUserThemeRequest request,
        PulumiApiClient apiClient,
        CancellationToken cancellationToken)
    {
        await apiClient.SetUserThemeAsync(request.Theme, cancellationToken);
        return Results.NoContent();
    }
}
