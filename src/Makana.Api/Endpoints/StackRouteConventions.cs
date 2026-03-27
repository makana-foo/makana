using System.Text.RegularExpressions;

namespace Makana.Api.Endpoints;

internal static partial class StackRouteConventions
{
    public const string StackRouteParameter = "{stack:regex(^[A-Za-z0-9._-]+$)}";
    public const string RelativeStackRoute = "/" + StackRouteParameter;
    public const string ApiStackRoute = "/api/stacks/{org}/{project}/" + StackRouteParameter;
    public const string InvalidStackNameMessage = "stack name may only contain alphanumeric characters, hyphens, underscores, or periods";

    public static bool IsValidStackName(string stack)
        => !string.IsNullOrWhiteSpace(stack) && StackNamePattern().IsMatch(stack);

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex StackNamePattern();
}
