using MediatR;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Search;
using ProjectManagement.Web.Models;

namespace ProjectManagement.Web.Controllers;

public class SavedSearchesController(ISender sender) : Controller
{
    private bool IsAjax => Request.Headers["X-Requested-With"] == "XMLHttpRequest";
    private bool ValidReturn(string? value) => string.IsNullOrEmpty(value) || (value.Length <= ReturnNavigation.MaxLength && Url.IsLocalUrl(value));

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int projectId, string? returnUrl, CancellationToken cancellationToken)
    {
        var list = await sender.Send(new ListSavedSearchesQuery(projectId), cancellationToken);
        if (list == null) return NotFound();
        if (!ValidReturn(returnUrl)) return BadRequest();
        var model = new SavedSearchListViewModel(list, returnUrl);
        if (!IsAjax) return View(model);
        Response.Headers["X-Workboard-Partial"] = "list";
        return PartialView("_List", model);
    }

    [HttpGet]
    public async Task<IActionResult> Run([FromQuery] int projectId, [FromRoute] int id, CancellationToken cancellationToken)
    {
        var filter = await sender.Send(new GetSavedSearchQuery(projectId, id), cancellationToken);
        return filter == null ? NotFound() : RedirectToAction("Index", "Search", new Microsoft.AspNetCore.Routing.RouteValueDictionary
            { ["projectId"] = projectId.ToString(System.Globalization.CultureInfo.InvariantCulture), ["Input.Expression"] = filter.Expression, ["Input.Ordering"] = filter.Ordering });
    }

    [HttpPost]
    public Task<IActionResult> Prepare([FromQuery] int projectId, SavedSearchFormViewModel model, CancellationToken cancellationToken)
        => CreateForm(projectId, model, cancellationToken);

    [HttpGet, ActionName("Create")]
    public async Task<IActionResult> CreateForm([FromQuery] int projectId, SavedSearchFormViewModel model, CancellationToken cancellationToken)
    {
        var list = await sender.Send(new ListSavedSearchesQuery(projectId), cancellationToken);
        if (list == null || !ValidReturn(model.ReturnUrl)) return NotFound();
        model.ProjectId = projectId; model.Project = list.Project; model.Action = "Create";
        if (model.Archived) ModelState.AddModelError("", "Saved searches apply to active work. Switch off archived work first.");
        if (list.Project.IsArchived) ModelState.AddModelError("", "Restore this project before saving searches.");
        return Form(model);
    }

    [HttpGet]
    public Task<IActionResult> Rename([FromQuery] int projectId, [FromRoute] int id, string? returnUrl, CancellationToken cancellationToken)
        => ExistingForm(projectId, id, returnUrl, "Rename", cancellationToken);

    [HttpGet]
    public Task<IActionResult> Delete([FromQuery] int projectId, [FromRoute] int id, string? returnUrl, CancellationToken cancellationToken)
        => ExistingForm(projectId, id, returnUrl, "Delete", cancellationToken);

    private async Task<IActionResult> ExistingForm(int projectId, int id, string? returnUrl, string action, CancellationToken cancellationToken)
    {
        var filter = await sender.Send(new GetSavedSearchQuery(projectId, id), cancellationToken);
        if (filter == null || !ValidReturn(returnUrl)) return NotFound();
        var model = new SavedSearchFormViewModel { ProjectId = projectId, Id = id, ExpectedVersion = filter.Version,
            Name = filter.Name, Project = filter.Project, Action = action, ReturnUrl = returnUrl };
        if (filter.Project.IsArchived) ModelState.AddModelError("", "Restore this project before changing searches.");
        return Form(model);
    }

    [HttpPost]
    public Task<IActionResult> Create([FromQuery] int projectId, SavedSearchFormViewModel model, CancellationToken cancellationToken)
        => Save(projectId, 0, model, "Create", cancellationToken);

    [HttpPost]
    public Task<IActionResult> Rename([FromQuery] int projectId, [FromRoute] int id, SavedSearchFormViewModel model, CancellationToken cancellationToken)
        => Save(projectId, id, model, "Rename", cancellationToken);

    [HttpPost]
    public Task<IActionResult> Delete([FromQuery] int projectId, [FromRoute] int id, SavedSearchFormViewModel model, CancellationToken cancellationToken)
        => Save(projectId, id, model, "Delete", cancellationToken);

    private async Task<IActionResult> Save(int projectId, int id, SavedSearchFormViewModel model, string action, CancellationToken cancellationToken)
    {
        if (projectId != model.ProjectId || (action != "Create" && id != model.Id)) return NotFound();
        var list = await sender.Send(new ListSavedSearchesQuery(projectId), cancellationToken);
        if (list == null) return NotFound();
        model.Project = list.Project; model.Action = action;
        if (action != "Create")
        {
            var filter = await sender.Send(new GetSavedSearchQuery(projectId, id), cancellationToken);
            if (filter == null) return NotFound();
            if (action == "Delete") { model.Name = filter.Name; ModelState.Remove(nameof(model.Name)); }
            if (model.ExpectedVersion < 1) ModelState.AddModelError(nameof(model.ExpectedVersion), "Reload this search before submitting a change.");
        }
        if (!ValidReturn(model.ReturnUrl)) { model.ReturnUrl = null; ModelState.Remove(nameof(model.ReturnUrl)); ModelState.AddModelError("", "Use a local return address."); }
        if (ModelState.IsValid)
        {
            var result = action switch
            {
                "Rename" => await sender.Send(new RenameSavedSearchCommand(projectId, id, model.ExpectedVersion, model.Name ?? ""), cancellationToken),
                "Delete" => await sender.Send(new DeleteSavedSearchCommand(projectId, id, model.ExpectedVersion), cancellationToken),
                _ => await sender.Send(new CreateSavedSearchCommand(projectId, model.Name ?? "", model.Expression ?? "", model.Ordering ?? "", model.Archived), cancellationToken)
            };
            if (result.Status == OperationStatus.NotFound) return NotFound();
            if (result.Status == OperationStatus.Success)
            {
                if (IsAjax) return Json(new { saved = true, message = action == "Delete" ? "Search deleted." : "Search saved." });
                TempData["Success"] = action == "Delete" ? "Search deleted." : "Search saved.";
                return !string.IsNullOrEmpty(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl) : RedirectToAction(nameof(Index), new { projectId });
            }
            Response.StatusCode = result.Status switch { OperationStatus.Conflict => 409, OperationStatus.Busy => 503, _ => 400 };
            ModelState.AddModelError(result.Field ?? "", result.Status == OperationStatus.Conflict
                ? "This search has changed. Close this form and reopen the current version before trying again." : result.Error!);
        }
        else Response.StatusCode = 400;
        return Form(model);
    }

    private IActionResult Form(SavedSearchFormViewModel model)
    {
        if (!IsAjax) return View("Form", model);
        Response.Headers["X-Workboard-Partial"] = "modal";
        return PartialView("_Form", model);
    }
}
