using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProjectManagement.Infrastructure;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Tests;

public class StorageConfigurationTests
{
    [Theory]
    [InlineData("projectmanagement.db", "ProjectManagement")]
    [InlineData("workboard.db", "Workboard")]
    public void StorageConfigurationKeepsTheSelectedDatabaseAndKeyPurpose(string fileName, string applicationName)
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddInfrastructure(directory, fileName, applicationName);
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var protection = provider.GetRequiredService<IOptions<DataProtectionOptions>>();

            // Assert
            Assert.Equal(Path.Combine(directory, fileName), database.Database.GetDbConnection().DataSource);
            Assert.Equal(applicationName, protection.Value.ApplicationDiscriminator);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("../other.db")]
    [InlineData("/tmp/other.db")]
    [InlineData("folder\\other.db")]
    [InlineData("..")]
    [InlineData("")]
    public void DatabaseNameCannotEscapeStorage(string fileName)
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var services = new ServiceCollection();

        // Act
        var action = () => services.AddInfrastructure(directory, fileName);

        // Assert
        Assert.Throws<ArgumentException>(action);
        Assert.False(Directory.Exists(directory));
    }
}
