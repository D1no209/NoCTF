using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.Configurations.Progression;

internal sealed class CompetitionProgressionConfiguration
    : IEntityTypeConfiguration<CompetitionProgression>
{
    public void Configure(EntityTypeBuilder<CompetitionProgression> builder)
    {
        builder.ToTable("competition_progressions");
        builder.HasKey(item => item.CompetitionId);
        builder.HasOne<Competition>().WithOne()
            .HasForeignKey<CompetitionProgression>(item => item.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(item => item.Nodes).WithOne()
            .HasForeignKey(item => item.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(item => item.Edges).WithOne()
            .HasForeignKey(item => item.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProgressionNodeConfiguration : IEntityTypeConfiguration<ProgressionNode>
{
    public void Configure(EntityTypeBuilder<ProgressionNode> builder)
    {
        builder.ToTable("competition_progression_nodes");
        builder.HasKey(item => item.Id);
        builder.Ignore(item => item.Kind);
        builder.HasDiscriminator<string>("kind")
            .HasValue<ChallengeProgressionNode>("challenge")
            .HasValue<BadgeProgressionNode>("badge");
        builder.Property<string>("kind").HasMaxLength(32);
        builder.HasIndex(item => item.CompetitionId);
    }
}

internal sealed class ChallengeProgressionNodeConfiguration
    : IEntityTypeConfiguration<ChallengeProgressionNode>
{
    public void Configure(EntityTypeBuilder<ChallengeProgressionNode> builder)
    {
        builder.HasIndex(item => item.CompetitionChallengeId).IsUnique();
        builder.HasOne<CompetitionChallenge>().WithMany()
            .HasForeignKey(item => item.CompetitionChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BadgeProgressionNodeConfiguration
    : IEntityTypeConfiguration<BadgeProgressionNode>
{
    public void Configure(EntityTypeBuilder<BadgeProgressionNode> builder)
    {
        builder.HasIndex(item => item.CompetitionBadgeId);
        builder.HasOne<CompetitionBadge>().WithMany()
            .HasForeignKey(item => item.CompetitionBadgeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProgressionEdgeConfiguration : IEntityTypeConfiguration<ProgressionEdge>
{
    public void Configure(EntityTypeBuilder<ProgressionEdge> builder)
    {
        builder.ToTable("competition_progression_edges");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Condition).HasConversion<short>();
        builder.HasIndex(item => new
            { item.CompetitionId, item.SourceNodeId, item.TargetNodeId }).IsUnique();
        builder.HasOne<ProgressionNode>().WithMany()
            .HasForeignKey(item => item.SourceNodeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProgressionNode>().WithMany()
            .HasForeignKey(item => item.TargetNodeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CompetitionBadgeConfiguration : IEntityTypeConfiguration<CompetitionBadge>
{
    public void Configure(EntityTypeBuilder<CompetitionBadge> builder)
    {
        builder.ToTable("competition_badges");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(160).IsRequired();
        builder.Property(item => item.Description).HasMaxLength(1000);
        builder.HasIndex(item => item.CompetitionId);
        builder.HasOne<Competition>().WithMany()
            .HasForeignKey(item => item.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.ImageFile).WithMany()
            .HasForeignKey(item => item.ImageFileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TeamProgressionNodeStateConfiguration
    : IEntityTypeConfiguration<TeamProgressionNodeState>
{
    public void Configure(EntityTypeBuilder<TeamProgressionNodeState> builder)
    {
        builder.ToTable("team_progression_node_states");
        builder.HasKey(item => new { item.TeamId, item.NodeId });
        builder.HasIndex(item => new { item.CompetitionId, item.TeamId });
        builder.HasOne<Team>().WithMany()
            .HasForeignKey(item => item.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProgressionNode>().WithMany()
            .HasForeignKey(item => item.NodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TeamProgressionNodeVisitConfiguration
    : IEntityTypeConfiguration<TeamProgressionNodeVisit>
{
    public void Configure(EntityTypeBuilder<TeamProgressionNodeVisit> builder)
    {
        builder.ToTable("team_progression_node_visits");
        builder.HasKey(item => new { item.TeamId, item.NodeId });
        builder.HasIndex(item => new { item.CompetitionId, item.TeamId });
        builder.HasOne<Team>().WithMany()
            .HasForeignKey(item => item.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProgressionNode>().WithMany()
            .HasForeignKey(item => item.NodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserBadgeGrantConfiguration : IEntityTypeConfiguration<UserBadgeGrant>
{
    public void Configure(EntityTypeBuilder<UserBadgeGrant> builder)
    {
        builder.ToTable("user_badge_grants");
        builder.HasKey(item => new { item.TeamId, item.BadgeId, item.UserId });
        builder.HasIndex(item => new { item.CompetitionId, item.UserId, item.Active });
        builder.HasOne<Team>().WithMany()
            .HasForeignKey(item => item.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompetitionBadge>().WithMany()
            .HasForeignKey(item => item.BadgeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TeamProgressionBadgeStateConfiguration
    : IEntityTypeConfiguration<TeamProgressionBadgeState>
{
    public void Configure(EntityTypeBuilder<TeamProgressionBadgeState> builder)
    {
        builder.ToTable("team_progression_badge_states");
        builder.HasKey(item => new { item.TeamId, item.BadgeId });
        builder.HasIndex(item => new { item.CompetitionId, item.BadgeId });
        builder.HasOne<Team>().WithMany()
            .HasForeignKey(item => item.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompetitionBadge>().WithMany()
            .HasForeignKey(item => item.BadgeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserBadgeTransitionConfiguration
    : IEntityTypeConfiguration<UserBadgeTransition>
{
    public void Configure(EntityTypeBuilder<UserBadgeTransition> builder)
    {
        builder.ToTable("user_badge_transitions");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Kind).HasConversion<short>();
        builder.HasIndex(item => new { item.CompetitionId, item.TeamId, item.OccurredAt });
        builder.HasIndex(item => new { item.UserId, item.BadgeId, item.OccurredAt });
        builder.HasOne<CompetitionBadge>().WithMany()
            .HasForeignKey(item => item.BadgeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany()
            .HasForeignKey(item => item.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
