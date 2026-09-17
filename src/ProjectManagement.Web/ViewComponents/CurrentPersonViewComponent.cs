using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Infrastructure.Repositories;

namespace ProjectManagement.Web.ViewComponents;

public class CurrentPersonViewComponent(ApplicationDbContext context, ICurrentUser currentUser) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var person = await PersonQueries.Select(context).SingleOrDefaultAsync(user => user.Id == currentUser.UserId);
        return View(person);
    }
}
