using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ProjectManagement.Domain.Entities;

namespace ProjectManagement.Infrastructure.Data;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects", table =>
        {
            table.HasCheckConstraint("CK_Project_Key", "length(Key) BETWEEN 2 AND 10 AND Key = upper(Key) AND Key NOT GLOB '*[^A-Z0-9]*' AND substr(Key, 1, 1) GLOB '[A-Z]'");
            table.HasCheckConstraint("CK_Project_Name", "length(trim(Name)) BETWEEN 1 AND 100");
            table.HasCheckConstraint("CK_Project_Description", "length(Description) <= 20000");
            table.HasCheckConstraint("CK_Project_Number", "NextWorkItemNumber > 0");
            table.HasCheckConstraint("CK_Project_Version", "Version > 0");
        });
        builder.HasIndex(project => project.Key).IsUnique();
        builder.Property(project => project.Key).HasMaxLength(10);
        builder.Property(project => project.Name).HasMaxLength(100);
        builder.Property(project => project.Description).HasMaxLength(20000);
        builder.Property(project => project.Version).IsConcurrencyToken();
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(project => project.OwnerId).OnDelete(DeleteBehavior.Restrict);
        var utc = new ValueConverter<DateTime, DateTime>(value => value.ToUniversalTime(), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.Property(project => project.CreatedAtUtc).HasConversion(utc);
        builder.Property(project => project.UpdatedAtUtc).HasConversion(utc);
    }
}

public class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("ProjectMembers");
        builder.HasKey(member => new { member.ProjectId, member.UserId });
        builder.HasOne<Project>().WithMany().HasForeignKey(member => member.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(member => member.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(member => member.JoinedAtUtc).HasConversion(
            value => value.ToUniversalTime(), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}

public class ChangeEventConfiguration : IEntityTypeConfiguration<ChangeEvent>
{
    public void Configure(EntityTypeBuilder<ChangeEvent> builder)
    {
        builder.ToTable("ChangeEvents", table => table.HasCheckConstraint("CK_Change_Target", "WorkItemId IS NULL OR DocumentId IS NULL"));
        builder.Property(change => change.Action).HasMaxLength(64);
        builder.Property(change => change.Summary).HasMaxLength(1000);
        builder.HasOne<Project>().WithMany().HasForeignKey(change => change.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(change => change.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(change => new { change.ProjectId, change.OccurredAtUtc, change.Id });
        builder.HasOne<WorkItem>().WithMany().HasForeignKey(change => new { change.ProjectId, change.WorkItemId })
            .HasPrincipalKey(work => new { work.ProjectId, work.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(change => new { change.ProjectId, change.WorkItemId, change.OccurredAtUtc, change.Id });
        builder.HasOne<ProjectDocument>().WithMany().HasForeignKey(change => new { change.ProjectId, change.DocumentId })
            .HasPrincipalKey(document => new { document.ProjectId, document.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(change => new { change.ProjectId, change.DocumentId, change.OccurredAtUtc, change.Id });
        builder.Property(change => change.OccurredAtUtc).HasConversion(
            value => value.ToUniversalTime(), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
