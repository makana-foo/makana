using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Makana.WebConsole.Endpoints;

internal static class UserPreferenceEndpoints
{
    public static IEndpointRouteBuilder MapUserPreferenceEndpoints(this IEndpointRouteBuilder app)
    {
        var userGroup = app.MapGroup("/api/user")
            .RequireAuthorization();

        userGroup.MapPut("/theme", HandleSetThemeAsync);

        return app;
    }

    private static async Task<NoContent> HandleSetThemeAsync(
        UpdateUserThemeRequest request,
        PulumiApiClient apiClient,
        CancellationToken cancellationToken)
    {
        await apiClient.SetUserThemeAsync(request.Theme, cancellationToken);
        return TypedResults.NoContent();
    }
}
