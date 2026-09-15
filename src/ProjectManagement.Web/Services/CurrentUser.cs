using System.Security.Claims;
using ProjectManagement.Application.Interfaces;

namespace ProjectManagement.Web.Services;

public class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public string UserId => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("An authenticated account is required.");
}
