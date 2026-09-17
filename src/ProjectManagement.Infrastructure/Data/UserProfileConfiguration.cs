using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectManagement.Domain.Entities;

namespace ProjectManagement.Infrastructure.Data;

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("UserProfiles", table =>
        {
            table.HasCheckConstraint("CK_Profile_FirstName", "length(trim(FirstName)) BETWEEN 1 AND 100");
            table.HasCheckConstraint("CK_Profile_LastName", "length(trim(LastName)) BETWEEN 1 AND 100");
        });
        builder.HasKey(profile => profile.UserId);
        builder.Property(profile => profile.FirstName).HasMaxLength(100);
        builder.Property(profile => profile.LastName).HasMaxLength(100);
        builder.HasOne<IdentityUser>().WithOne().HasForeignKey<UserProfile>(profile => profile.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
