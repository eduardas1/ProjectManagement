using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Infrastructure.Repositories;
using ProjectManagement.Application.Interfaces;

namespace ProjectManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string storageDirectory,
        string databaseFileName = "projectmanagement.db", string applicationName = "ProjectManagement")
    {
        if (string.IsNullOrWhiteSpace(databaseFileName) || databaseFileName != Path.GetFileName(databaseFileName)
            || databaseFileName is "." or ".." || databaseFileName.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("Storage database must be a file name.", nameof(databaseFileName));
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        Directory.CreateDirectory(storageDirectory);
        var keysDirectory = Directory.CreateDirectory(Path.Combine(storageDirectory, "keys"));
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(storageDirectory, databaseFileName),
            ForeignKeys = true,
            DefaultTimeout = 5
        }.ToString();

        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));
        services.AddDataProtection().SetApplicationName(applicationName).PersistKeysToFileSystem(keysDirectory);
        services.AddIdentity<IdentityUser, IdentityRole>(options =>
        {
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version1;
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<ApplicationDbContext>();

        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IWorkItemRepository, WorkItemRepository>();
        services.AddScoped<IWorkItemReadRepository, WorkItemReadRepository>();
        services.AddScoped<IWorkSearchRepository, WorkSearchRepository>();
        services.AddScoped<IProjectProgressRepository, ProjectProgressRepository>();
        services.AddScoped<ISavedSearchRepository, SavedSearchRepository>();
        services.AddScoped<ILabelRepository, LabelRepository>();
        services.AddScoped<ICollaborationRepository, CollaborationRepository>();
        services.AddScoped<ICollaborationReadRepository, CollaborationReadRepository>();
        services.AddScoped<ProjectManagement.Application.WorkItems.WorkItemValidation>();
        services.AddScoped<IProjectAccess, ProjectAccess>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<ISearchDefinitionValidator, ProjectManagement.Infrastructure.Search.WorkSearchSyntax>();
        services.AddScoped<ProjectManagement.Infrastructure.Search.WorkItemSearchMapper>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
