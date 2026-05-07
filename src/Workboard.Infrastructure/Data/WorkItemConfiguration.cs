using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workboard.Domain.Entities;

namespace Workboard.Infrastructure.Data;

public class WorkItemConfiguration : IEntityTypeConfiguration<WorkItem>
{
    public void Configure(EntityTypeBuilder<WorkItem> builder)
    {
        builder.ToTable("WorkItems", table =>
        {
            table.HasCheckConstraint("CK_Work_Number", "Number > 0");
            table.HasCheckConstraint("CK_Work_Title", "length(trim(Title)) BETWEEN 1 AND 200");
            table.HasCheckConstraint("CK_Work_Description", "length(Description) <= 20000");
            table.HasCheckConstraint("CK_Work_Type", "Type IN ('Bug', 'Task', 'Story', 'Epic', 'Subtask')");
            table.HasCheckConstraint("CK_Work_Status", "Status IN ('Backlog', 'Todo', 'InProgress', 'InReview', 'Done', 'Cancelled')");
            table.HasCheckConstraint("CK_Work_Priority", "Priority IN ('Low', 'Medium', 'High', 'Critical')");
            table.HasCheckConstraint("CK_Work_Hierarchy", "(Type = 'Subtask' AND ParentId IS NOT NULL AND EpicId IS NULL) OR (Type IN ('Bug', 'Task', 'Story') AND ParentId IS NULL) OR (Type = 'Epic' AND ParentId IS NULL AND EpicId IS NULL)");
            table.HasCheckConstraint("CK_Work_Self", "(ParentId IS NULL OR ParentId <> Id) AND (EpicId IS NULL OR EpicId <> Id)");
            table.HasCheckConstraint("CK_Work_Version", "Version > 0");
        });
        builder.HasAlternateKey(work => new { work.ProjectId, work.Id });
        builder.HasIndex(work => new { work.ProjectId, work.Number }).IsUnique();
        builder.HasIndex(work => new { work.ProjectId, work.IsArchived, work.Status, work.Id });
        builder.HasIndex(work => new { work.ProjectId, work.IsArchived, work.AssigneeId, work.Id });
        builder.HasIndex(work => new { work.ProjectId, work.IsArchived, work.DueDate, work.Id });
        builder.Property(work => work.Title).HasMaxLength(200);
        builder.Property(work => work.Description).HasMaxLength(20000);
        builder.Property(work => work.Type).HasConversion<string>();
        builder.Property(work => work.Status).HasConversion<string>();
        builder.Property(work => work.Priority).HasConversion<string>();
        builder.Property(work => work.Version).IsConcurrencyToken();
        builder.HasOne<Project>().WithMany().HasForeignKey(work => work.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(work => work.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(work => work.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WorkItem>().WithMany().HasForeignKey(work => new { work.ProjectId, work.ParentId })
            .HasPrincipalKey(work => new { work.ProjectId, work.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WorkItem>().WithMany().HasForeignKey(work => new { work.ProjectId, work.EpicId })
            .HasPrincipalKey(work => new { work.ProjectId, work.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(work => work.CreatedAtUtc).HasConversion(
            value => value.ToUniversalTime(), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.Property(work => work.UpdatedAtUtc).HasConversion(
            value => value.ToUniversalTime(), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}

public class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    public void Configure(EntityTypeBuilder<Label> builder)
    {
        builder.ToTable("Labels", table =>
        {
            table.HasCheckConstraint("CK_Label_Name", "length(trim(Name)) BETWEEN 1 AND 40");
            table.HasCheckConstraint("CK_Label_NormalizedName", "length(NormalizedName) BETWEEN 1 AND 40");
            table.HasCheckConstraint("CK_Label_Version", "Version > 0");
        });
        builder.HasAlternateKey(label => new { label.ProjectId, label.Id });
        builder.HasIndex(label => new { label.ProjectId, label.NormalizedName }).IsUnique();
        builder.Property(label => label.Name).HasMaxLength(40);
        builder.Property(label => label.NormalizedName).HasMaxLength(40);
        builder.Property(label => label.Version).IsConcurrencyToken();
        builder.HasOne<Project>().WithMany().HasForeignKey(label => label.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class WorkItemLabelConfiguration : IEntityTypeConfiguration<WorkItemLabel>
{
    public void Configure(EntityTypeBuilder<WorkItemLabel> builder)
    {
        builder.ToTable("WorkItemLabels");
        builder.HasKey(label => new { label.ProjectId, label.WorkItemId, label.LabelId });
        builder.HasIndex(label => new { label.ProjectId, label.LabelId, label.WorkItemId });
        builder.HasOne<WorkItem>().WithMany(work => work.Labels).HasForeignKey(label => new { label.ProjectId, label.WorkItemId })
            .HasPrincipalKey(work => new { work.ProjectId, work.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(label => label.Label).WithMany().HasForeignKey(label => new { label.ProjectId, label.LabelId })
            .HasPrincipalKey(label => new { label.ProjectId, label.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public class WorkItemLinkConfiguration : IEntityTypeConfiguration<WorkItemLink>
{
    public void Configure(EntityTypeBuilder<WorkItemLink> builder)
    {
        builder.ToTable("WorkItemLinks", table =>
        {
            table.HasCheckConstraint("CK_Link_Kind", "Kind IN ('Blocks', 'Duplicates')");
            table.HasCheckConstraint("CK_Link_Self", "SourceId <> TargetId");
        });
        builder.Property(link => link.Kind).HasConversion<string>();
        builder.HasIndex(link => new { link.ProjectId, link.SourceId, link.TargetId, link.Kind }).IsUnique();
        builder.HasIndex(link => new { link.ProjectId, link.SourceId }).IsUnique().HasFilter("Kind = 'Duplicates'");
        builder.HasIndex(link => new { link.ProjectId, link.TargetId });
        builder.HasOne<WorkItem>().WithMany().HasForeignKey(link => new { link.ProjectId, link.SourceId })
            .HasPrincipalKey(work => new { work.ProjectId, work.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WorkItem>().WithMany().HasForeignKey(link => new { link.ProjectId, link.TargetId })
            .HasPrincipalKey(work => new { work.ProjectId, work.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(link => link.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.Property(link => link.CreatedAtUtc).HasConversion(
            value => value.ToUniversalTime(), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
