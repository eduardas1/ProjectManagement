using MediatR;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Documents;
using ProjectManagement.Web.Models;

namespace ProjectManagement.Web.Controllers;

public class DocumentsController(ISender sender) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int projectId, int page = 1,
        CancellationToken cancellationToken = default, string? sort = null, bool descending = false, int pageSize = 20)
    {
        if (!ModelState.IsValid) return BadRequest("Invalid document list request.");
        var documents = await sender.Send(new ListDocumentsQuery(projectId, page, new TableOptions(sort ?? "", descending, pageSize)), cancellationToken);
        return documents == null ? NotFound() : View(documents);
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromQuery] int projectId, [FromRoute] int id, CancellationToken cancellationToken)
    {
        var document = await sender.Send(new GetDocumentQuery(projectId, id), cancellationToken);
        if (document == null) return NotFound();
        if (!IsAjax || Request.Headers["X-Workboard-View"] != "document") return View(document);
        Response.Headers["X-Workboard-Partial"] = "document";
        return PartialView("_Details", document);
    }

    [HttpGet]
    public async Task<IActionResult> Revisions([FromQuery] int projectId, [FromRoute] int id, int page = 1,
        CancellationToken cancellationToken = default, string? sort = null, bool descending = false, int pageSize = 20)
    {
        if (!ModelState.IsValid) return BadRequest("Invalid revision list request.");
        var revisions = await sender.Send(new ListRevisionsQuery(projectId, id, page, new TableOptions(sort ?? "", descending, pageSize)), cancellationToken);
        if (revisions == null) return NotFound();
        if (!IsAjax) return View(revisions);
        Response.Headers["X-Workboard-Partial"] = "list";
        return PartialView("_Revisions", revisions);
    }

    [HttpGet]
    public async Task<IActionResult> Revision([FromQuery] int projectId, [FromQuery] int documentId, [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var revision = await sender.Send(new GetRevisionQuery(projectId, documentId, id), cancellationToken);
        if (revision == null) return NotFound();
        if (!IsAjax) return View(revision);
        Response.Headers["X-Workboard-Partial"] = "list";
        return PartialView("_Revision", revision);
    }

    [HttpGet]
    public async Task<IActionResult> Create([FromQuery] int projectId, CancellationToken cancellationToken)
    {
        var project = await sender.Send(new GetDocumentProjectQuery(projectId), cancellationToken);
        if (project == null) return NotFound();
        if (project.IsArchived) return RedirectToAction(nameof(Index), new { projectId });
        return View(new CreateDocumentViewModel { ProjectId = projectId, Project = project });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromQuery] int projectId, CreateDocumentViewModel model, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId) return NotFound();
        var project = await sender.Send(new GetDocumentProjectQuery(projectId), cancellationToken);
        if (project == null) return NotFound();
        model.Project = project;
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new CreateDocumentCommand(projectId, model.Title, model.Body, model.Format), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = "Document created.";
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
        var details = await sender.Send(new GetDocumentQuery(projectId, id), cancellationToken);
        if (details == null) return NotFound();
        if (details.Project.IsArchived || details.Document.IsArchived) return RedirectToAction(nameof(Details), new { projectId, id });
        return View(new EditDocumentViewModel { ProjectId = projectId, DocumentId = id, Title = details.Document.Title,
            Body = details.Body, Format = details.Format, ExpectedVersion = details.Document.Version, Project = details.Project });
    }

    [HttpPost]
    public async Task<IActionResult> Edit([FromQuery] int projectId, [FromRoute] int id, EditDocumentViewModel model, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId || id != model.DocumentId) return NotFound();
        var details = await sender.Send(new GetDocumentQuery(projectId, id), cancellationToken);
        if (details == null) return NotFound();
        model.Project = details.Project;
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new UpdateDocumentCommand(projectId, id, model.ExpectedVersion, model.Title, model.Body, model.Format), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = "Document saved.";
                return RedirectToAction(nameof(Details), new { projectId, id });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }



    [HttpPost]
    public async Task<IActionResult> Preview(DocumentPreviewViewModel model, CancellationToken cancellationToken)
    {
        var project = await sender.Send(new GetDocumentProjectQuery(model.ProjectId), cancellationToken);
        if (project == null) return NotFound();
        if (!ModelState.IsValid) return BadRequest("Invalid preview request.");
        Response.Headers["X-Workboard-Partial"] = "document-preview";
        return Content(ProjectManagement.Web.Services.DocumentRenderer.Render(model.Body ?? "", model.Format), "text/html");
    }

    private bool IsAjax => Request.Headers["X-Requested-With"] == "XMLHttpRequest";


    private void ApplyError(OperationResult<int> result)
    {
        Response.StatusCode = result.Status switch
        {
            OperationStatus.Conflict => StatusCodes.Status409Conflict,
            OperationStatus.Busy => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        ModelState.AddModelError(result.Field ?? "", result.Status == OperationStatus.Conflict
            ? "This document has changed. Reload it before submitting another change." : result.Error ?? "The change could not be saved.");
    }
}
