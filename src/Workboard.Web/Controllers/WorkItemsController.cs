using MediatR;
using Microsoft.AspNetCore.Mvc;
using Workboard.Application.Common;
using Workboard.Application.WorkItems;
using Workboard.Web.Models;
using Workboard.Domain.Enums;

namespace Workboard.Web.Controllers;

public class WorkItemsController(ISender sender) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int projectId, bool archived = false, int page = 1, WorkItemStatus? status = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || (status.HasValue && !Enum.IsDefined(status.Value))) return BadRequest("Select a valid work status.");
        var list = await sender.Send(new ListWorkItemsQuery(projectId, archived, page, status), cancellationToken);
        return list == null ? NotFound() : View(list);
    }

    [HttpGet]
    public async Task<IActionResult> Backlog([FromQuery] int projectId, int page = 1, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest("Invalid work list request.");
        var list = await sender.Send(new ListWorkItemsQuery(projectId, false, page, WorkItemStatus.Backlog), cancellationToken);
        return list == null ? NotFound() : View("Index", list);
    }

    [HttpGet]
    public async Task<IActionResult> Cancelled([FromQuery] int projectId, int page = 1, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest("Invalid work list request.");
        var list = await sender.Send(new ListWorkItemsQuery(projectId, false, page, WorkItemStatus.Cancelled), cancellationToken);
        return list == null ? NotFound() : View("Index", list);
    }

    [HttpGet]
    public async Task<IActionResult> Board([FromQuery] int projectId, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest("Invalid board request.");
        var board = await sender.Send(new GetWorkBoardQuery(projectId), cancellationToken);
        if (board == null) return NotFound();
        if (!IsAjax || Request.Headers["X-Workboard-View"] == "workspace") return View(board);
        Response.Headers["X-Workboard-Partial"] = "list";
        return PartialView("_Board", board);
    }

    [HttpGet]
    public async Task<IActionResult> Status([FromQuery] int projectId, [FromRoute] int id, string? returnUrl, CancellationToken cancellationToken)
    {
        var details = await sender.Send(new GetWorkItemQuery(projectId, id), cancellationToken);
        if (details == null) return NotFound();
        if (returnUrl != null && (returnUrl.Length > ReturnNavigation.MaxLength || !Url.IsLocalUrl(returnUrl))) return BadRequest("Return address must be local.");
        if (details.Project.IsArchived || details.Item.IsArchived) return RedirectToAction(nameof(Details), new { projectId, id });
        return StatusForm(new WorkItemStatusViewModel { ProjectId = projectId, WorkItemId = id,
            ExpectedVersion = details.Item.Version, Details = details, ReturnUrl = returnUrl });
    }

    [HttpPost]
    public async Task<IActionResult> Status([FromQuery] int projectId, [FromRoute] int id, WorkItemStatusViewModel model, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId || id != model.WorkItemId) return NotFound();
        var details = await sender.Send(new GetWorkItemQuery(projectId, id), cancellationToken);
        if (details == null) return NotFound();
        model.Details = details;
        if (model.ReturnUrl != null && !Url.IsLocalUrl(model.ReturnUrl)) ModelState.AddModelError(nameof(model.ReturnUrl), "Return address must be local.");
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new ChangeWorkItemStatusCommand(projectId, id, model.ExpectedVersion, model.Status!.Value), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                if (IsAjax) return Json(new { saved = true, message = "Work status saved." });
                TempData["Success"] = "Work status saved.";
                return model.ReturnUrl == null ? RedirectToAction(nameof(Details), new { projectId, id }) : LocalRedirect(model.ReturnUrl);
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return StatusForm(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromQuery] int projectId, [FromRoute] int id, CancellationToken cancellationToken)
    {
        var work = await sender.Send(new GetWorkItemQuery(projectId, id), cancellationToken);
        if (work == null) return NotFound();
        if (Request.Headers["X-Workboard-View"] != "preview") return View(work);
        Response.Headers["X-Workboard-Partial"] = "preview";
        return PartialView("_Details", work);
    }

    [HttpGet]
    public async Task<IActionResult> Create([FromQuery] int projectId, CancellationToken cancellationToken)
    {
        var options = await sender.Send(new GetWorkItemOptionsQuery(projectId), cancellationToken);
        if (options == null) return NotFound();
        if (options.Project.IsArchived) return RedirectToAction(nameof(Index), new { projectId });
        return View(new CreateWorkItemViewModel { ProjectId = projectId, Options = options });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromQuery] int projectId, CreateWorkItemViewModel model, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId) return NotFound();
        var options = await sender.Send(new GetWorkItemOptionsQuery(projectId), cancellationToken);
        if (options == null) return NotFound();
        model.Options = options;
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new CreateWorkItemCommand(projectId, model.Type, model.Title, model.Description ?? "",
                model.Priority, model.AssigneeId, model.DueDate, model.ParentId, model.EpicId, model.LabelIds ?? []), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = "Work item created.";
                return RedirectToAction(nameof(Details), new { projectId, id = result.Value });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit([FromQuery] int projectId, [FromRoute] int id, CancellationToken cancellationToken)
    {
        var details = await sender.Send(new GetWorkItemQuery(projectId, id), cancellationToken);
        if (details == null) return NotFound();
        if (details.Project.IsArchived || details.Item.IsArchived) return RedirectToAction(nameof(Details), new { projectId, id });
        var options = await sender.Send(new GetWorkItemOptionsQuery(projectId), cancellationToken);
        if (options == null) return NotFound();
        var work = details.Item;
        return View(new EditWorkItemViewModel
        {
            ProjectId = projectId, WorkItemId = id, ExpectedVersion = work.Version, Title = work.Title,
            Description = work.Description, Priority = work.Priority, AssigneeId = work.AssigneeId, DueDate = work.DueDate,
            LabelIds = details.Labels.Select(label => label.Id).ToArray(), WorkKey = $"{details.Project.Key}-{work.Number}", Options = options
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit([FromQuery] int projectId, [FromRoute] int id, EditWorkItemViewModel model, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId || id != model.WorkItemId) return NotFound();
        var details = await sender.Send(new GetWorkItemQuery(projectId, id), cancellationToken);
        if (details == null) return NotFound();
        var options = await sender.Send(new GetWorkItemOptionsQuery(projectId), cancellationToken);
        if (options == null) return NotFound();
        model.Options = options;
        model.WorkKey = $"{details.Project.Key}-{details.Item.Number}";
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new UpdateWorkItemCommand(projectId, id, model.ExpectedVersion, model.Title,
                model.Description ?? "", model.Priority, model.AssigneeId, model.DueDate, model.LabelIds ?? []), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = "Work item saved.";
                return RedirectToAction(nameof(Details), new { projectId, id });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Hierarchy([FromQuery] int projectId, [FromRoute] int id, CancellationToken cancellationToken)
    {
        var details = await sender.Send(new GetWorkItemQuery(projectId, id), cancellationToken);
        if (details == null) return NotFound();
        if (details.Project.IsArchived || details.Item.IsArchived || details.Item.Type == Workboard.Domain.Enums.WorkItemType.Epic)
            return RedirectToAction(nameof(Details), new { projectId, id });
        var options = await sender.Send(new GetWorkItemOptionsQuery(projectId), cancellationToken);
        if (options == null) return NotFound();
        return View(new WorkItemHierarchyViewModel { ProjectId = projectId, WorkItemId = id, ExpectedVersion = details.Item.Version,
            ParentId = details.Item.ParentId, EpicId = details.Item.EpicId, Details = details, Options = options });
    }

    [HttpPost]
    public async Task<IActionResult> Hierarchy([FromQuery] int projectId, [FromRoute] int id, WorkItemHierarchyViewModel model, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId || id != model.WorkItemId) return NotFound();
        var details = await sender.Send(new GetWorkItemQuery(projectId, id), cancellationToken);
        if (details == null) return NotFound();
        var options = await sender.Send(new GetWorkItemOptionsQuery(projectId), cancellationToken);
        if (options == null) return NotFound();
        model.Details = details; model.Options = options;
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new ChangeWorkItemHierarchyCommand(projectId, id, model.ExpectedVersion, model.ParentId, model.EpicId), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = "Work hierarchy saved.";
                return RedirectToAction(nameof(Details), new { projectId, id });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> AddLink([FromQuery] int projectId, [FromRoute] int id, CancellationToken cancellationToken)
    {
        var details = await sender.Send(new GetWorkItemQuery(projectId, id), cancellationToken);
        if (details == null) return NotFound();
        if (details.Project.IsArchived || details.Item.IsArchived) return RedirectToAction(nameof(Details), new { projectId, id });
        var options = await sender.Send(new GetWorkItemOptionsQuery(projectId), cancellationToken);
        if (options == null) return NotFound();
        return View(new WorkItemLinkViewModel { ProjectId = projectId, WorkItemId = id, ExpectedVersion = details.Item.Version,
            Details = details, Options = options });
    }

    [HttpPost]
    public async Task<IActionResult> AddLink([FromQuery] int projectId, [FromRoute] int id, WorkItemLinkViewModel model, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId || id != model.WorkItemId) return NotFound();
        var details = await sender.Send(new GetWorkItemQuery(projectId, id), cancellationToken);
        if (details == null) return NotFound();
        var options = await sender.Send(new GetWorkItemOptionsQuery(projectId), cancellationToken);
        if (options == null) return NotFound();
        model.Details = details; model.Options = options;
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new AddWorkItemLinkCommand(projectId, id, model.ExpectedVersion, model.TargetId, model.Kind), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = "Relationship added.";
                return RedirectToAction(nameof(Details), new { projectId, id });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> RemoveLink([FromQuery] int projectId, [FromRoute] int id,
        [FromQuery] int targetId, [FromQuery] Workboard.Domain.Enums.WorkItemLinkKind kind, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !Enum.IsDefined(kind)) return NotFound();
        var details = await sender.Send(new GetWorkItemQuery(projectId, id), cancellationToken);
        if (details == null) return NotFound();
        var link = details.Connections.SingleOrDefault(link => link.IsOutgoing && link.TargetId == targetId && link.Kind == kind);
        if (link == null) return NotFound();
        if (details.Project.IsArchived || details.Item.IsArchived || link.IsArchived)
            return RedirectToAction(nameof(Details), new { projectId, id });
        return View(new WorkItemLinkViewModel { ProjectId = projectId, WorkItemId = id, ExpectedVersion = details.Item.Version,
            TargetId = targetId, Kind = kind, TargetName = $"{details.Project.Key}-{link.Number} {link.Title}", Details = details });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveLink([FromQuery] int projectId, [FromRoute] int id, WorkItemLinkViewModel model, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId || id != model.WorkItemId) return NotFound();
        var details = await sender.Send(new GetWorkItemQuery(projectId, id), cancellationToken);
        if (details == null) return NotFound();
        var target = await sender.Send(new GetWorkItemQuery(projectId, model.TargetId), cancellationToken);
        model.Details = details;
        model.TargetName = target == null ? "Selected work item" : $"{target.Project.Key}-{target.Item.Number} {target.Item.Title}";
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new RemoveWorkItemLinkCommand(projectId, id, model.ExpectedVersion, model.TargetId, model.Kind), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = "Relationship removed.";
                return RedirectToAction(nameof(Details), new { projectId, id });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Archive([FromQuery] int projectId, [FromRoute] int id, CancellationToken cancellationToken)
    {
        var details = await sender.Send(new GetWorkItemQuery(projectId, id), cancellationToken);
        if (details == null || !details.Project.IsOwner) return NotFound();
        if (details.Project.IsArchived) return RedirectToAction(nameof(Details), new { projectId, id });
        return View(new WorkItemArchiveViewModel { ProjectId = projectId, WorkItemId = id, ExpectedVersion = details.Item.Version,
            IsArchived = !details.Item.IsArchived, Details = details });
    }

    [HttpPost]
    public async Task<IActionResult> Archive([FromQuery] int projectId, [FromRoute] int id, WorkItemArchiveViewModel model, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId || id != model.WorkItemId) return NotFound();
        var details = await sender.Send(new GetWorkItemQuery(projectId, id), cancellationToken);
        if (details == null || !details.Project.IsOwner) return NotFound();
        model.Details = details;
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new SetWorkItemArchiveCommand(projectId, id, model.ExpectedVersion, model.IsArchived), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = model.IsArchived ? "Work item archived." : "Work item restored.";
                return RedirectToAction(nameof(Details), new { projectId, id });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }

    private bool IsAjax => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    private IActionResult StatusForm(WorkItemStatusViewModel model)
    {
        if (!IsAjax) return View("Status", model);
        Response.Headers["X-Workboard-Partial"] = "modal";
        return PartialView("_Status", model);
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
            ? "This work item has changed. Reload it before submitting another change." : result.Error ?? "The change could not be saved.");
    }
}
