using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ProjectManagement.Domain.Entities;

namespace ProjectManagement.Infrastructure.Data;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("Comments", table =>
        {
            table.HasCheckConstraint("CK_Comment_Removal", "(IsDeleted = 0 AND length(trim(Body)) BETWEEN 1 AND 5000 AND DeletedById IS NULL AND DeletedAtUtc IS NULL AND ModerationReason IS NULL) OR (IsDeleted = 1 AND Body = '' AND DeletedById IS NOT NULL AND DeletedAtUtc IS NOT NULL)");
            table.HasCheckConstraint("CK_Comment_Reason", "(ModerationReason IS NULL OR length(ModerationReason) <= 500) AND (IsDeleted = 0 OR DeletedById = AuthorId OR length(trim(coalesce(ModerationReason, ''))) > 0)");
            table.HasCheckConstraint("CK_Comment_Version", "Version > 0");
        });
        builder.Property(comment => comment.Body).HasMaxLength(5000);
        builder.Property(comment => comment.ModerationReason).HasMaxLength(500);
        builder.Property(comment => comment.Version).IsConcurrencyToken();
        builder.HasOne<WorkItem>().WithMany().HasForeignKey(comment => new { comment.ProjectId, comment.WorkItemId })
            .HasPrincipalKey(work => new { work.ProjectId, work.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(comment => comment.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(comment => comment.DeletedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(comment => new { comment.ProjectId, comment.WorkItemId, comment.CreatedAtUtc, comment.Id });
        var utc = new ValueConverter<DateTime, DateTime>(value => value.ToUniversalTime(), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.Property(comment => comment.CreatedAtUtc).HasConversion(utc);
        builder.Property(comment => comment.UpdatedAtUtc).HasConversion(utc);
        builder.Property(comment => comment.DeletedAtUtc).HasConversion(utc);
    }
}

public class ProjectDocumentConfiguration : IEntityTypeConfiguration<ProjectDocument>
{
    public void Configure(EntityTypeBuilder<ProjectDocument> builder)
    {
        builder.ToTable("ProjectDocuments", table =>
        {
            table.HasCheckConstraint("CK_Document_Title", "length(trim(Title)) BETWEEN 1 AND 200");
            table.HasCheckConstraint("CK_Document_Body", "length(Body) <= 100000");
            table.HasCheckConstraint("CK_Document_Version", "Version > 0");
        });
        builder.HasAlternateKey(document => new { document.ProjectId, document.Id });
        builder.Property(document => document.Title).HasMaxLength(200);
        builder.Property(document => document.Body).HasMaxLength(100000);
        builder.Property(document => document.Format).HasDefaultValue(ProjectManagement.Domain.Enums.DocumentFormat.PlainText);
        builder.Property(document => document.Version).IsConcurrencyToken();
        builder.HasOne<Project>().WithMany().HasForeignKey(document => document.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(document => document.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(document => document.UpdatedById).OnDelete(DeleteBehavior.Restrict);
        var utc = new ValueConverter<DateTime, DateTime>(value => value.ToUniversalTime(), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.Property(document => document.CreatedAtUtc).HasConversion(utc);
        builder.Property(document => document.UpdatedAtUtc).HasConversion(utc);
    }
}

public class DocumentRevisionConfiguration : IEntityTypeConfiguration<DocumentRevision>
{
    public void Configure(EntityTypeBuilder<DocumentRevision> builder)
    {
        builder.ToTable("DocumentRevisions", table =>
        {
            table.HasCheckConstraint("CK_Revision_Title", "length(trim(Title)) BETWEEN 1 AND 200");
            table.HasCheckConstraint("CK_Revision_Body", "length(Body) <= 100000");
            table.HasCheckConstraint("CK_Revision_Version", "Version > 0");
        });
        builder.Property(revision => revision.Title).HasMaxLength(200);
        builder.Property(revision => revision.Body).HasMaxLength(100000);
        builder.Property(revision => revision.Format).HasDefaultValue(ProjectManagement.Domain.Enums.DocumentFormat.PlainText);
        builder.HasIndex(revision => new { revision.DocumentId, revision.Version }).IsUnique();
        builder.HasOne<ProjectDocument>().WithMany().HasForeignKey(revision => new { revision.ProjectId, revision.DocumentId })
            .HasPrincipalKey(document => new { document.ProjectId, document.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(revision => revision.EditorId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(revision => revision.SavedAtUtc).HasConversion(
            value => value.ToUniversalTime(), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments", table =>
        {
            table.HasCheckConstraint("CK_Attachment_Target", "(WorkItemId IS NOT NULL AND DocumentId IS NULL) OR (WorkItemId IS NULL AND DocumentId IS NOT NULL)");
            table.HasCheckConstraint("CK_Attachment_Length", "Length BETWEEN 1 AND 5242880 AND typeof(Contents) = 'blob' AND length(Contents) = Length");
            table.HasCheckConstraint("CK_Attachment_Name", "length(StoredName) BETWEEN 1 AND 64 AND length(DisplayName) BETWEEN 1 AND 255");
            table.HasCheckConstraint("CK_Attachment_Type", "ContentType IN ('application/pdf', 'image/png', 'image/jpeg', 'text/plain', 'text/csv')");
            table.HasCheckConstraint("CK_Attachment_Version", "Version > 0");
        });
        builder.Property(attachment => attachment.StoredName).HasMaxLength(64);
        builder.Property(attachment => attachment.DisplayName).HasMaxLength(255);
        builder.Property(attachment => attachment.Version).IsConcurrencyToken();
        builder.HasIndex(attachment => attachment.StoredName).IsUnique();
        builder.HasOne<WorkItem>().WithMany().HasForeignKey(attachment => new { attachment.ProjectId, attachment.WorkItemId })
            .HasPrincipalKey(work => new { work.ProjectId, work.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProjectDocument>().WithMany().HasForeignKey(attachment => new { attachment.ProjectId, attachment.DocumentId })
            .HasPrincipalKey(document => new { document.ProjectId, document.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(attachment => attachment.UploadedById).OnDelete(DeleteBehavior.Restrict);
        builder.Property(attachment => attachment.UploadedAtUtc).HasConversion(
            value => value.ToUniversalTime(), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
