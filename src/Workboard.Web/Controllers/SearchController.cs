using MediatR;
using Microsoft.AspNetCore.Mvc;
using Workboard.Application.Common;
using Workboard.Application.Search;
using Workboard.Web.Models;

namespace Workboard.Web.Controllers;

public class SearchController(ISender sender) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int projectId, [FromQuery] WorkSearchViewModel model, CancellationToken cancellationToken)
    {
        var options = await sender.Send(new GetWorkSearchOptionsQuery(projectId), cancellationToken);
        if (options == null) return NotFound();
        model.Options = options;
        model.Input ??= new WorkSearchInput();
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new SearchWorkItemsQuery(projectId, model.Input), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success) model.Results = result.Value;
            else ModelState.AddModelError(string.IsNullOrEmpty(result.Field) ? "" : "Input." + result.Field, result.Error!);
        }
        if (!ModelState.IsValid) Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }
}
