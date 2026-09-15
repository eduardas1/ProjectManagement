using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Infrastructure.Identity;

namespace ProjectManagement.Infrastructure.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();

        var entries = configuration.GetSection("InitialUsers").Get<List<InitialUser>>() ?? [];
        if (entries.Count == 0)
        {
            return;
        }

        var manager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usersToCreate = new List<(IdentityUser User, string Password)>();
        foreach (var entry in entries)
        {
            var email = entry.Email?.Trim() ?? "";
            if (email.Length > 254 || !new EmailAddressAttribute().IsValid(email)
                || string.IsNullOrEmpty(entry.Password) || entry.Password.Length > 1024
                || !emails.Add(manager.NormalizeEmail(email)))
            {
                throw new InvalidOperationException("Initial user configuration is invalid.");
            }

            var byEmail = await manager.FindByEmailAsync(email);
            var byName = await manager.FindByNameAsync(email);
            if ((byEmail == null) != (byName == null) || byEmail?.Id != byName?.Id)
            {
                throw new InvalidOperationException("Initial user configuration conflicts with an existing account.");
            }

            var user = byEmail ?? new IdentityUser { UserName = email, Email = email };
            foreach (var validator in manager.PasswordValidators)
            {
                var validation = await validator.ValidateAsync(manager, user, entry.Password);
                if (!validation.Succeeded)
                {
                    throw new InvalidOperationException("Initial user password does not meet the password policy.");
                }
            }

            if (byEmail == null)
            {
                usersToCreate.Add((user, entry.Password));
            }
        }

        await using var transaction = await context.Database.BeginTransactionAsync();
        foreach (var entry in usersToCreate)
        {
            var result = await manager.CreateAsync(entry.User, entry.Password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("Initial user creation failed.");
            }
        }
        await transaction.CommitAsync();
    }
}
