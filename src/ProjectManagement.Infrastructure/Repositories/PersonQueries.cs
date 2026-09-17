using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Common;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Infrastructure.Repositories;

public static class PersonQueries
{
    public static IQueryable<PersonSummary> Select(ApplicationDbContext context)
        => from user in context.Users.AsNoTracking()
           join profile in context.UserProfiles.AsNoTracking() on user.Id equals profile.UserId into profiles
           from profile in profiles.DefaultIfEmpty()
           select new PersonSummary
           {
               Id = user.Id,
               FirstName = profile == null ? "" : profile.FirstName,
               LastName = profile == null ? "" : profile.LastName,
               Email = user.Email ?? user.UserName ?? ""
           };
}
