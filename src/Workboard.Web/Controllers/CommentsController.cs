using MediatR;
using Microsoft.AspNetCore.Mvc;
using Workboard.Application.Comments;
using Workboard.Application.Common;
using Workboard.Web.Models;

namespace Workboard.Web.Controllers;

public class CommentsController(ISender sender) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int projectId, [FromQuery] int workItemId, int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest("Invalid comment list request.");
        var comments = await sender.Send(new ListCommentsQuery(projectId, workItemId, page), cancellationToken);
        return comments == null ? NotFound() : View(comments);
    }

    [HttpGet]
    public async Task<IActionResult> Create([FromQuery] int projectId, [FromQuery] int workItemId, CancellationToken cancellationToken)
    {
        var work = await sender.Send(new GetCommentWorkQuery(projectId, workItemId), cancellationToken);
        if (work == null) return NotFound();
        if (work.Project.IsArchived || work.IsArchived) return RedirectToAction(nameof(Index), new { projectId, workItemId });
        return View(new CreateCommentViewModel { ProjectId = projectId, WorkItemId = workItemId, Work = work });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromQuery] int projectId, [FromQuery] int workItemId,
        CreateCommentViewModel model, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId || workItemId != model.WorkItemId) return NotFound();
        var work = await sender.Send(new GetCommentWorkQuery(projectId, workItemId), cancellationToken);
        if (work == null) return NotFound();
        model.Work = work;
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new CreateCommentCommand(projectId, workItemId, model.Body), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = "Comment added.";
                return RedirectToAction(nameof(Index), new { projectId, workItemId });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit([FromQuery] int projectId, [FromQuery] int workItemId, [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var details = await sender.Send(new GetCommentQuery(projectId, workItemId, id), cancellationToken);
        if (details == null || !details.Comment.IsAuthor) return NotFound();
        if (details.Work.Project.IsArchived || details.Work.IsArchived || details.Comment.IsDeleted)
            return RedirectToAction(nameof(Index), new { projectId, workItemId });
        return View(new EditCommentViewModel { ProjectId = projectId, WorkItemId = workItemId, CommentId = id,
            ExpectedVersion = details.Comment.Version, Body = details.Comment.Body, Work = details.Work });
    }

    [HttpPost]
    public async Task<IActionResult> Edit([FromQuery] int projectId, [FromQuery] int workItemId, [FromRoute] int id,
        EditCommentViewModel model, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId || workItemId != model.WorkItemId || id != model.CommentId) return NotFound();
        var details = await sender.Send(new GetCommentQuery(projectId, workItemId, id), cancellationToken);
        if (details == null || !details.Comment.IsAuthor) return NotFound();
        model.Work = details.Work;
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new UpdateCommentCommand(projectId, workItemId, id, model.ExpectedVersion, model.Body), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = "Comment saved.";
                return RedirectToAction(nameof(Index), new { projectId, workItemId });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Remove([FromQuery] int projectId, [FromQuery] int workItemId, [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var details = await sender.Send(new GetCommentQuery(projectId, workItemId, id), cancellationToken);
        if (details == null || (!details.Comment.IsAuthor && !details.Work.Project.IsOwner)) return NotFound();
        if (details.Work.Project.IsArchived || details.Work.IsArchived || details.Comment.IsDeleted)
            return RedirectToAction(nameof(Index), new { projectId, workItemId });
        return View(new RemoveCommentViewModel { ProjectId = projectId, WorkItemId = workItemId, CommentId = id,
            ExpectedVersion = details.Comment.Version, Details = details });
    }

    [HttpPost]
    public async Task<IActionResult> Remove([FromQuery] int projectId, [FromQuery] int workItemId, [FromRoute] int id,
        RemoveCommentViewModel model, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId || workItemId != model.WorkItemId || id != model.CommentId) return NotFound();
        var details = await sender.Send(new GetCommentQuery(projectId, workItemId, id), cancellationToken);
        if (details == null || (!details.Comment.IsAuthor && !details.Work.Project.IsOwner)) return NotFound();
        model.Details = details;
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new RemoveCommentCommand(projectId, workItemId, id, model.ExpectedVersion, model.ModerationReason), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = "Comment removed.";
                return RedirectToAction(nameof(Index), new { projectId, workItemId });
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
        ModelState.AddModelError(result.Field ?? "", result.Status == OperationStatus.Conflict
            ? "This comment has changed. Reload it before submitting another change." : result.Error ?? "The change could not be saved.");
    }
}
