using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ProjectManagement.Domain.Entities;

namespace ProjectManagement.Infrastructure.Data;

public class SavedFilterConfiguration : IEntityTypeConfiguration<SavedFilter>
{
    public void Configure(EntityTypeBuilder<SavedFilter> builder)
    {
        builder.ToTable("SavedFilters", table =>
        {
            table.HasCheckConstraint("CK_SavedFilter_Name", "length(trim(Name)) BETWEEN 1 AND 100");
            table.HasCheckConstraint("CK_SavedFilter_NormalizedName", "length(NormalizedName) BETWEEN 1 AND 100");
            table.HasCheckConstraint("CK_SavedFilter_Expression", "length(Expression) <= 2000");
            table.HasCheckConstraint("CK_SavedFilter_Ordering", "length(Ordering) <= 200");
            table.HasCheckConstraint("CK_SavedFilters_Version", "Version > 0");
        });
        builder.Property(filter => filter.Name).HasMaxLength(100);
        builder.Property(filter => filter.NormalizedName).HasMaxLength(100);
        builder.Property(filter => filter.Expression).HasMaxLength(2000);
        builder.Property(filter => filter.Ordering).HasMaxLength(200);
        builder.Property(filter => filter.Version).IsConcurrencyToken();
        builder.HasIndex(filter => new { filter.ProjectId, filter.OwnerId, filter.NormalizedName }).IsUnique();
        builder.HasOne<Project>().WithMany().HasForeignKey(filter => filter.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(filter => filter.OwnerId).OnDelete(DeleteBehavior.Restrict);
        var utc = new ValueConverter<DateTime, DateTime>(value => value.ToUniversalTime(), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.Property(filter => filter.CreatedAtUtc).HasConversion(utc);
        builder.Property(filter => filter.UpdatedAtUtc).HasConversion(utc);
    }
}
