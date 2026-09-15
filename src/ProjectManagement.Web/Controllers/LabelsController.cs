using MediatR;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Labels;
using ProjectManagement.Application.WorkItems;
using ProjectManagement.Web.Models;

namespace ProjectManagement.Web.Controllers;

public class LabelsController(ISender sender) : Controller
{
    private bool IsAjax => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int projectId, CancellationToken cancellationToken)
    {
        var labels = await sender.Send(new ListLabelsQuery(projectId), cancellationToken);
        if (labels == null) return NotFound();
        if (!IsAjax) return View(labels);
        Response.Headers["X-Workboard-Partial"] = "list";
        return PartialView("_List", labels);
    }

    [HttpGet]
    public async Task<IActionResult> Create([FromQuery] int projectId, CancellationToken cancellationToken)
    {
        var labels = await sender.Send(new ListLabelsQuery(projectId), cancellationToken);
        if (labels == null) return NotFound();
        if (labels.Project.IsArchived) ModelState.AddModelError("", "Restore this project before creating labels.");
        return Form("Create", new CreateLabelViewModel { ProjectId = projectId, Project = labels.Project });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromQuery] int projectId, CreateLabelViewModel model, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId) return NotFound();
        var labels = await sender.Send(new ListLabelsQuery(projectId), cancellationToken);
        if (labels == null) return NotFound();
        model.Project = labels.Project;
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new CreateLabelCommand(projectId, model.Name), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                return Saved(projectId, "Label created.");
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return Form("Create", model);
    }

    [HttpGet]
    public async Task<IActionResult> Rename([FromQuery] int projectId, [FromRoute] int id, CancellationToken cancellationToken)
    {
        var labels = await sender.Send(new ListLabelsQuery(projectId), cancellationToken);
        var label = labels?.Items.SingleOrDefault(item => item.Id == id);
        if (labels == null || label == null) return NotFound();
        if (labels.Project.IsArchived) ModelState.AddModelError("", "Restore this project before renaming labels.");
        return Form("Rename", new RenameLabelViewModel { ProjectId = projectId, LabelId = id, Name = label.Name,
            ExpectedVersion = label.Version, Project = labels.Project });
    }

    [HttpPost]
    public async Task<IActionResult> Rename([FromQuery] int projectId, [FromRoute] int id, RenameLabelViewModel model, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId || id != model.LabelId) return NotFound();
        var labels = await sender.Send(new ListLabelsQuery(projectId), cancellationToken);
        if (labels == null || !labels.Items.Any(label => label.Id == id)) return NotFound();
        model.Project = labels.Project;
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new RenameLabelCommand(projectId, id, model.ExpectedVersion, model.Name), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                return Saved(projectId, "Label renamed.");
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return Form("Rename", model);
    }

    [HttpGet]
    public async Task<IActionResult> Delete([FromQuery] int projectId, [FromRoute] int id, CancellationToken cancellationToken)
    {
        var labels = await sender.Send(new ListLabelsQuery(projectId), cancellationToken);
        var label = labels?.Items.SingleOrDefault(item => item.Id == id);
        if (labels == null || !labels.Project.IsOwner || label == null) return NotFound();
        if (labels.Project.IsArchived) ModelState.AddModelError("", "Restore this project before deleting labels.");
        return Form("Delete", new DeleteLabelViewModel { ProjectId = projectId, LabelId = id, ExpectedVersion = label.Version,
            Name = label.Name, UsageCount = label.UsageCount, Project = labels.Project });
    }

    [HttpPost]
    public async Task<IActionResult> Delete([FromQuery] int projectId, [FromRoute] int id, DeleteLabelViewModel model, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId || id != model.LabelId) return NotFound();
        var labels = await sender.Send(new ListLabelsQuery(projectId), cancellationToken);
        var label = labels?.Items.SingleOrDefault(item => item.Id == id);
        if (labels == null || !labels.Project.IsOwner || label == null) return NotFound();
        model.Project = labels.Project; model.Name = label.Name; model.UsageCount = label.UsageCount;
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new DeleteLabelCommand(projectId, id, model.ExpectedVersion), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                return Saved(projectId, "Unused label deleted.");
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return Form("Delete", model);
    }

    private IActionResult Form(string action, object model)
    {
        if (!IsAjax) return View(action, model);
        Response.Headers["X-Workboard-Partial"] = "modal";
        return PartialView("_" + action, model);
    }

    private IActionResult Saved(int projectId, string message)
    {
        if (IsAjax) return Json(new { saved = true, message });
        TempData["Success"] = message;
        return RedirectToAction(nameof(Index), new { projectId });
    }

    private void ApplyError(OperationResult<int> result)
    {
        Response.StatusCode = result.Status switch
        {
            OperationStatus.Conflict => StatusCodes.Status409Conflict,
            OperationStatus.Busy => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        ModelState.AddModelError(result.Field ?? "", result.Status == OperationStatus.Conflict
            ? "This label has changed. Reload it before submitting another change." : result.Error ?? "The change could not be saved.");
    }
}
