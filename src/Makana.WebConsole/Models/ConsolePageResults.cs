using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Makana.WebConsole.Models;

public static class ConsolePageResults
{
    private const string ConsoleNotFoundViewPath = "/Pages/Shared/ConsoleNotFound.cshtml";

    private static readonly ConsoleEmptyStateViewModel DefaultNotFoundModel =
        new("We couldn't find that console page.", "Not found");

    private static readonly ConsoleEmptyStateViewModel OwnerRouteNotFoundModel =
        new("That owner-scoped console page is not available for this signed-in user.", "Not found");

    public static IActionResult OwnerRouteNotFound(PageModel pageModel)
        => NotFound(pageModel, OwnerRouteNotFoundModel);

    public static IActionResult ConsoleNotFound(PageModel pageModel)
        => NotFound(pageModel, DefaultNotFoundModel);

    private static IActionResult NotFound(PageModel pageModel, ConsoleEmptyStateViewModel model)
        => new ViewResult
        {
            StatusCode = StatusCodes.Status404NotFound,
            ViewName = ConsoleNotFoundViewPath,
            ViewData = new ViewDataDictionary<ConsoleEmptyStateViewModel>(
                new EmptyModelMetadataProvider(),
                pageModel.ModelState)
            {
                Model = model
            }
        };
}
