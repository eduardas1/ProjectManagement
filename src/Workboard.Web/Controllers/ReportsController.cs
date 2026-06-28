using MediatR;
using Microsoft.AspNetCore.Mvc;
using Workboard.Application.Common;
using Workboard.Application.Reports;
using Workboard.Application.Search;
using Workboard.Web.Models;

namespace Workboard.Web.Controllers;

public class ReportsController(ISender sender) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int projectId, [FromQuery] ProjectProgressViewModel model, CancellationToken cancellationToken)
    {
        var project = await sender.Send(new GetReportProjectQuery(projectId), cancellationToken);
        if (project == null) return NotFound();
        model.Project = project;
        model.Input ??= new WorkSearchInput();
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new GetProjectProgressQuery(projectId, model.Input), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success) model.Progress = result.Value;
            else ModelState.AddModelError(string.IsNullOrEmpty(result.Field) ? "" : "Input." + result.Field, result.Error!);
        }
        if (!ModelState.IsValid) Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }
}
