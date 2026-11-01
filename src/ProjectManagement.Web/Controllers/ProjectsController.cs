using MediatR;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Projects;
using ProjectManagement.Application.WorkItems;
using ProjectManagement.Web.Models;

namespace ProjectManagement.Web.Controllers;

public class ProjectsController(ISender sender) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(bool archived = false, CancellationToken cancellationToken = default)
    {
        ViewData["Archived"] = archived;
        return View(await sender.Send(new ListProjectsQuery(archived), cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var project = await sender.Send(new GetProjectQuery(id), cancellationToken);
        if (project == null) return NotFound();
        if (!IsAjax || Request.Headers["X-Workboard-View"] != "project") return View(project);
        Response.Headers["X-Workboard-Partial"] = "project";
        return PartialView("_Details", project);
    }

    [HttpGet]
    public IActionResult Create() => View(new CreateProjectViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(CreateProjectViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new CreateProjectCommand(model.Key, model.Name, model.Description ?? ""), cancellationToken);
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = "Project created.";
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var project = await sender.Send(new GetProjectQuery(id), cancellationToken);
        if (project == null || !project.IsOwner) return NotFound();
        ViewData["WorkProject"] = new WorkProject(project.Id, project.Key, project.Name, project.IsArchived, project.IsOwner);
        if (project.IsArchived) return RedirectToAction(nameof(Details), new { id });
        return View(new EditProjectViewModel
        {
            ProjectId = id, ExpectedVersion = project.Version, Key = project.Key,
            Name = project.Name, Description = project.Description
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, EditProjectViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.ProjectId) return NotFound();
        var project = await sender.Send(new GetProjectQuery(id), cancellationToken);
        if (project == null || !project.IsOwner) return NotFound();
        ViewData["WorkProject"] = new WorkProject(project.Id, project.Key, project.Name, project.IsArchived, project.IsOwner);
        model.Key = project.Key;
        ModelState.Remove(nameof(model.Key));
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new UpdateProjectCommand(id, model.ExpectedVersion, model.Name, model.Description ?? ""), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                TempData["Success"] = "Project details saved.";
                return RedirectToAction(nameof(Details), new { id });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Archive(int id, CancellationToken cancellationToken)
    {
        var project = await sender.Send(new GetProjectQuery(id), cancellationToken);
        if (project == null || !project.IsOwner) return NotFound();
        return ArchiveForm(new ArchiveProjectViewModel
        {
            ProjectId = id, ExpectedVersion = project.Version, Name = project.Name,
            Key = project.Key, IsArchived = !project.IsArchived
        });
    }

    [HttpPost]
    public async Task<IActionResult> Archive(int id, ArchiveProjectViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.ProjectId) return NotFound();
        var project = await sender.Send(new GetProjectQuery(id), cancellationToken);
        if (project == null || !project.IsOwner) return NotFound();
        model.Name = project.Name;
        model.Key = project.Key;
        ModelState.Remove(nameof(model.Name));
        ModelState.Remove(nameof(model.Key));
        if (ModelState.IsValid)
        {
            var result = await sender.Send(new SetProjectArchiveCommand(id, model.ExpectedVersion, model.IsArchived), cancellationToken);
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                if (IsAjax) return Json(new { saved = true, redirectUrl = Url.Action(nameof(Details), new { id }) });
                TempData["Success"] = model.IsArchived ? "Project archived." : "Project restored.";
                return RedirectToAction(nameof(Details), new { id });
            }
            ApplyError(result);
        }
        else Response.StatusCode = StatusCodes.Status400BadRequest;
        return ArchiveForm(model);
    }

    private bool IsAjax => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    private IActionResult ArchiveForm(ArchiveProjectViewModel model)
    {
        if (!IsAjax) return View("Archive", model);
        Response.Headers["X-Workboard-Partial"] = "modal";
        return PartialView("_Archive", model);
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
