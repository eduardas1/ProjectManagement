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
        if (project == null) return NotFound();
        if (!IsAjax) return View(project);
        Response.Headers["X-Workboard-Partial"] = "list";
        return PartialView("_List", project);
    }

    [HttpGet]
    public async Task<IActionResult> Add(int id, CancellationToken cancellationToken)
    {
        var project = await sender.Send(new GetProjectMembersQuery(id), cancellationToken);
        if (project == null) return NotFound();
        if (project.IsArchived) return RedirectToAction(nameof(Index), new { id });
        return MemberForm("Add", new AddMemberViewModel { ProjectId = id, ProjectKey = project.ProjectKey, ExpectedVersion = project.Version });
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
                if (IsAjax) return Json(new { saved = true, message = "Project member added." });
                TempData["Success"] = "Project member added.";
                return RedirectToAction(nameof(Index), new { id });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return MemberForm("Add", model);
    }

    [HttpGet]
    public async Task<IActionResult> Remove(int id, string userId, CancellationToken cancellationToken)
    {
        var project = await sender.Send(new GetProjectMembersQuery(id), cancellationToken);
        if (project == null) return NotFound();
        var member = project.Participants.SingleOrDefault(item => item.UserId == userId);
        if (member == null || member.IsOwner) return NotFound();
        return MemberForm("Remove", new RemoveMemberViewModel
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
                if (IsAjax) return Json(new { saved = true, message = "Project membership revoked." });
                TempData["Success"] = "Project membership revoked.";
                return RedirectToAction(nameof(Index), new { id });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return MemberForm("Remove", model);
    }

    [HttpGet]
    public async Task<IActionResult> Transfer(int id, CancellationToken cancellationToken)
    {
        var project = await sender.Send(new GetProjectMembersQuery(id), cancellationToken);
        if (project == null) return NotFound();
        return MemberForm("Transfer", new TransferOwnershipViewModel
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
                if (IsAjax) return Json(new { saved = true, message = "Project ownership transferred.", redirectUrl = Url.Action("Details", "Projects", new { id }) });
                TempData["Success"] = "Project ownership transferred.";
                return RedirectToAction("Details", "Projects", new { id });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return MemberForm("Transfer", model);
    }

    private bool IsAjax => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    private IActionResult MemberForm(string name, object model)
    {
        if (!IsAjax) return View(name, model);
        Response.Headers["X-Workboard-Partial"] = "modal";
        return PartialView("_" + name, model);
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
