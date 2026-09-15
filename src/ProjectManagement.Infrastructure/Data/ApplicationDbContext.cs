using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Domain.Entities;

namespace ProjectManagement.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<ChangeEvent> ChangeEvents => Set<ChangeEvent>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<WorkItemLabel> WorkItemLabels => Set<WorkItemLabel>();
    public DbSet<WorkItemLink> WorkItemLinks => Set<WorkItemLink>();

    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
    public DbSet<DocumentRevision> DocumentRevisions => Set<DocumentRevision>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<SavedFilter> SavedFilters => Set<SavedFilter>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new ProjectConfiguration());
        builder.ApplyConfiguration(new ProjectMemberConfiguration());
        builder.ApplyConfiguration(new ChangeEventConfiguration());
        builder.ApplyConfiguration(new WorkItemConfiguration());
        builder.ApplyConfiguration(new LabelConfiguration());
        builder.ApplyConfiguration(new WorkItemLabelConfiguration());
        builder.ApplyConfiguration(new WorkItemLinkConfiguration());
        builder.ApplyConfiguration(new CommentConfiguration());
        builder.ApplyConfiguration(new ProjectDocumentConfiguration());
        builder.ApplyConfiguration(new DocumentRevisionConfiguration());
        builder.ApplyConfiguration(new AttachmentConfiguration());
        builder.ApplyConfiguration(new SavedFilterConfiguration());
    }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }
}
