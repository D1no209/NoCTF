using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.Configurations.Teams;

internal sealed class TeamProfileConfiguration : IEntityTypeConfiguration<TeamProfile>
{
    public void Configure(EntityTypeBuilder<TeamProfile> builder)
    {
        builder.ToTable("team_profiles");
        builder.HasKey(profile => profile.Id);
        builder.HasQueryFilter(profile => profile.DeletedAt == null);
        builder.Property(profile => profile.MemberIds).HasColumnType("uuid[]");
        builder.HasIndex(profile => profile.NormalizedName).IsUnique()
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(profile => profile.InvitationToken).IsUnique();
        builder.HasIndex(profile => profile.MemberIds).HasMethod("gin");
        builder.HasOne<NoCTF.Domain.Identity.User>().WithMany()
            .HasForeignKey(profile => profile.CaptainId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_team_profiles_captain_is_member",
                "captain_id = ANY(member_ids)");
            table.HasCheckConstraint(
                "ck_team_profiles_members_not_empty",
                "cardinality(member_ids) > 0");
            table.HasCheckConstraint(
                "ck_team_profiles_invitation_token_length",
                "char_length(invitation_token) = 32");
        });
    }
}
