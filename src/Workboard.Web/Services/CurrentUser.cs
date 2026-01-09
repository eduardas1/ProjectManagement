using System.Security.Claims;
using Workboard.Application.Interfaces;

namespace Workboard.Web.Services;

public class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public string UserId => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("An authenticated account is required.");
}
