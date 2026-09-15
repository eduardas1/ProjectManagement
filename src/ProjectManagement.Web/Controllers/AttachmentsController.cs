using MediatR;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Application.Attachments;
using ProjectManagement.Application.Common;
using ProjectManagement.Web.Models;

namespace ProjectManagement.Web.Controllers;

public class AttachmentsController(ISender sender) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int projectId, [FromQuery] int? workItemId, [FromQuery] int? documentId,
        int page = 1, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest("Invalid attachment list request.");
        var files = await sender.Send(new ListAttachmentsQuery(projectId, workItemId, documentId, page), cancellationToken);
        if (files == null) return NotFound();
        if (!IsAjax) return View(files);
        Response.Headers["X-Workboard-Partial"] = "list";
        return PartialView("_List", files);
    }

    [HttpGet]
    public async Task<IActionResult> Upload([FromQuery] int projectId, [FromQuery] int? workItemId, [FromQuery] int? documentId,
        CancellationToken cancellationToken)
    {
        var target = await sender.Send(new GetAttachmentTargetQuery(projectId, workItemId, documentId), cancellationToken);
        if (target == null) return NotFound();
        if (target.Project.IsArchived || target.IsArchived) return RedirectToAction(nameof(Index), new { projectId, workItemId, documentId });
        return AttachmentForm("Upload", new UploadAttachmentViewModel { ProjectId = projectId, WorkItemId = workItemId, DocumentId = documentId, Target = target });
    }

    [HttpPost, RequestSizeLimit(AttachmentFile.TransportLimit), RequestFormLimits(MultipartBodyLengthLimit = AttachmentFile.TransportLimit)]
    public async Task<IActionResult> Upload([FromQuery] int projectId, [FromQuery] int? workItemId, [FromQuery] int? documentId,
        UploadAttachmentViewModel model, CancellationToken cancellationToken)
    {
        if (Request.ContentLength > AttachmentFile.TransportLimit)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, "The upload request is too large.");
        if (!ModelState.IsValid && model.ProjectId == 0)
            return BadRequest("The upload form could not be read. Select one file no larger than 5 MiB.");
        if (projectId != model.ProjectId || workItemId != model.WorkItemId || documentId != model.DocumentId) return NotFound();
        var target = await sender.Send(new GetAttachmentTargetQuery(projectId, workItemId, documentId), cancellationToken);
        if (target == null) return NotFound();
        model.Target = target;
        if (ModelState.IsValid && Request.Form.Files.Count != 1) ModelState.AddModelError(nameof(model.File), "Select one file.");
        if (ModelState.IsValid)
        {
            OperationResult<byte[]> read;
            try
            {
                await using var stream = model.File!.OpenReadStream();
                read = await AttachmentFile.ReadAsync(stream, model.File.Length, cancellationToken);
            }
            catch (IOException)
            {
                read = OperationResult<byte[]>.Invalid("File", "The file could not be read. Select it again.");
            }
            if (read.Status == OperationStatus.Success)
            {
                var result = await sender.Send(new UploadAttachmentCommand(projectId, workItemId, documentId, model.File!.FileName, read.Value!), cancellationToken);
                if (result.Status == OperationStatus.NotFound) return NotFound();
                if (result.Status == OperationStatus.Success)
                {
                    if (IsAjax) return Json(new { saved = true, message = "File uploaded." });
                    TempData["Success"] = "File uploaded.";
                    return RedirectToAction(nameof(Index), new { projectId, workItemId, documentId });
                }
                ApplyError(result);
            }
            else
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                ModelState.AddModelError(read.Field ?? "File", read.Error ?? "The file could not be read.");
            }
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return AttachmentForm("Upload", model);
    }

    [HttpGet]
    public async Task<IActionResult> Download([FromQuery] int projectId, [FromRoute] int id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        var file = await sender.Send(new DownloadAttachmentQuery(projectId, id), cancellationToken);
        return file == null ? NotFound() : File(file.Contents, file.ContentType, file.DisplayName);
    }

    [HttpGet]
    public async Task<IActionResult> Remove([FromQuery] int projectId, [FromRoute] int id, CancellationToken cancellationToken)
    {
        var details = await sender.Send(new GetAttachmentQuery(projectId, id), cancellationToken);
        if (details == null || !details.Attachment.CanRemove) return NotFound();
        if (details.Target.Project.IsArchived || details.Target.IsArchived)
            return RedirectToAction(nameof(Index), new { projectId, details.Target.WorkItemId, details.Target.DocumentId });
        return AttachmentForm("Remove", new RemoveAttachmentViewModel { ProjectId = projectId, AttachmentId = id,
            ExpectedVersion = details.Attachment.Version, Details = details });
    }

    [HttpPost]
    public async Task<IActionResult> Remove([FromQuery] int projectId, [FromRoute] int id, RemoveAttachmentViewModel model,
        CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId || id != model.AttachmentId) return NotFound();
        var details = await sender.Send(new GetAttachmentQuery(projectId, id), cancellationToken);
        if (details == null || !details.Attachment.CanRemove) return NotFound();
        model.Details = details;
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new RemoveAttachmentCommand(projectId, id, model.ExpectedVersion), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                if (IsAjax) return Json(new { saved = true, message = "File removed." });
                TempData["Success"] = "File removed.";
                return RedirectToAction(nameof(Index), new { projectId, details.Target.WorkItemId, details.Target.DocumentId });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return AttachmentForm("Remove", model);
    }

    private bool IsAjax => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    private IActionResult AttachmentForm(string name, object model)
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
        ModelState.AddModelError(result.Field ?? "", result.Status == OperationStatus.Conflict
            ? "This file has changed. Reload it before submitting another change." : result.Error ?? "The change could not be saved.");
    }
}
