using MediatR;
using Microsoft.AspNetCore.Mvc;
using Workboard.Application.Common;
using Workboard.Application.Projects;
using Workboard.Web.Models;

namespace Workboard.Web.Controllers;

public class MembersController(ISender sender) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int id, CancellationToken cancellationToken)
    {
        var project = await sender.Send(new GetProjectMembersQuery(id), cancellationToken);
        return project == null ? NotFound() : View(project);
    }

    [HttpGet]
    public async Task<IActionResult> Add(int id, CancellationToken cancellationToken)
    {
        var project = await sender.Send(new GetProjectMembersQuery(id), cancellationToken);
        if (project == null) return NotFound();
        if (project.IsArchived) return RedirectToAction(nameof(Index), new { id });
        return View(new AddMemberViewModel { ProjectId = id, ProjectKey = project.ProjectKey, ExpectedVersion = project.Version });
    }

    [HttpPost]
    public async Task<IActionResult> Add(int id, AddMemberViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.ProjectId) return NotFound();
        var project = await sender.Send(new GetProjectMembersQuery(id), cancellationToken);
        if (project == null) return NotFound();
        model.ProjectKey = project.ProjectKey;
        ModelState.Remove(nameof(model.ProjectKey));
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new AddProjectMemberCommand(id, model.ExpectedVersion, model.Email), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = "Project member added.";
                return RedirectToAction(nameof(Index), new { id });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Remove(int id, string userId, CancellationToken cancellationToken)
    {
        var project = await sender.Send(new GetProjectMembersQuery(id), cancellationToken);
        if (project == null) return NotFound();
        var member = project.Participants.SingleOrDefault(item => item.UserId == userId);
        if (member == null || member.IsOwner) return NotFound();
        return View(new RemoveMemberViewModel
        {
            ProjectId = id, ProjectKey = project.ProjectKey, ExpectedVersion = project.Version, UserId = userId, Email = member.Email
        });
    }

    [HttpPost]
    public async Task<IActionResult> Remove(int id, RemoveMemberViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.ProjectId) return NotFound();
        var project = await sender.Send(new GetProjectMembersQuery(id), cancellationToken);
        if (project == null) return NotFound();
        model.ProjectKey = project.ProjectKey;
        model.Email = project.Participants.SingleOrDefault(item => item.UserId == model.UserId)?.Email ?? "";
        ModelState.Remove(nameof(model.ProjectKey));
        ModelState.Remove(nameof(model.Email));
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new RemoveProjectMemberCommand(id, model.ExpectedVersion, model.UserId), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = "Project membership revoked.";
                return RedirectToAction(nameof(Index), new { id });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Transfer(int id, CancellationToken cancellationToken)
    {
        var project = await sender.Send(new GetProjectMembersQuery(id), cancellationToken);
        if (project == null) return NotFound();
        return View(new TransferOwnershipViewModel
        {
            ProjectId = id, ProjectKey = project.ProjectKey, ExpectedVersion = project.Version,
            Participants = project.Participants.Where(member => !member.IsOwner).ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> Transfer(int id, TransferOwnershipViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.ProjectId) return NotFound();
        var project = await sender.Send(new GetProjectMembersQuery(id), cancellationToken);
        if (project == null) return NotFound();
        model.ProjectKey = project.ProjectKey;
        model.Participants = project.Participants.Where(member => !member.IsOwner).ToList();
        ModelState.Remove(nameof(model.ProjectKey));
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new TransferProjectOwnershipCommand(id, model.ExpectedVersion, model.UserId), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = "Project ownership transferred.";
                return RedirectToAction("Details", "Projects", new { id });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }

    private void ApplyError(OperationResult<int> result)
    {
        Response.StatusCode = result.Status switch
        {
            OperationStatus.Conflict => StatusCodes.Status409Conflict,
            OperationStatus.Busy => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        ModelState.AddModelError(result.Field ?? "", result.Error ?? "The change could not be saved.");
    }
}
