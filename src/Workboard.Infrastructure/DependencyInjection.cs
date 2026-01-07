using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Workboard.Infrastructure.Data;

namespace Workboard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string storageDirectory)
    {
        Directory.CreateDirectory(storageDirectory);
        var keysDirectory = Directory.CreateDirectory(Path.Combine(storageDirectory, "keys"));
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(storageDirectory, "workboard.db"),
            ForeignKeys = true,
            DefaultTimeout = 5
        }.ToString();

        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));
        services.AddDataProtection().SetApplicationName("Workboard").PersistKeysToFileSystem(keysDirectory);
        services.AddIdentity<IdentityUser, IdentityRole>(options =>
        {
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version1;
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<ApplicationDbContext>();

        return services;
    }
}
