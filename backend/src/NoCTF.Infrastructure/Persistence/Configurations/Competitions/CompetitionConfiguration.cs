using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Competitions;

internal sealed class CompetitionEntityConfiguration : IEntityTypeConfiguration<Competition>
{
    public void Configure(EntityTypeBuilder<Competition> builder)
    {
        builder.ToTable("competitions");
        builder.HasKey(competition => competition.Id);
        builder.Property(competition => competition.Title).HasMaxLength(160);
        builder.Property(competition => competition.ConfigurationJson).HasColumnType("jsonb");
        builder.Property(competition => competition.ManagerIds).HasColumnType("uuid[]");
        builder.Property(competition => competition.JudgeIds).HasColumnType("uuid[]");
        builder.Property(competition => competition.ObserverIds).HasColumnType("uuid[]");
        builder.Property(competition => competition.Mode).HasConversion<short>();
        builder.Property(competition => competition.Status).HasConversion<short>();
        builder.Property(competition => competition.LeaderboardVisibility).HasConversion<short>();
        builder.Property(competition => competition.FrozenLeaderboardSnapshotJson).HasColumnType("jsonb");
        // Schema defaults preserve the documented cross-mode question policy when an existing
        // competition row is upgraded; Data Annotations cannot express database defaults.
        builder.Property(competition => competition.MaxActiveQuestionsPerTeam).HasDefaultValue(5);
        builder.Property(competition => competition.MaxParticipantMessagesBeforeHandlerReply).HasDefaultValue(3);
        builder.Property(competition => competition.AllowChallengeOwnersToHandleQuestions).HasDefaultValue(true);
        builder.HasQueryFilter(competition => competition.DeletedAt == null);
        builder.HasIndex(competition => new { competition.Status, competition.StartAt });
        builder.HasIndex(competition => new
        {
            competition.LeaderboardVisibility,
            competition.LeaderboardVisibilityStartsAt
        });
        builder.HasIndex(competition => competition.ManagerIds).HasMethod("gin");
        builder.HasIndex(competition => competition.JudgeIds).HasMethod("gin");
        builder.HasIndex(competition => competition.ObserverIds).HasMethod("gin");
        builder.HasOne<NoCTF.Domain.Identity.User>().WithMany()
            .HasForeignKey(competition => competition.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(competition => competition.PosterFile).WithMany()
            .HasForeignKey(competition => competition.PosterFileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_competitions_schedule",
            "start_at < end_at"));
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_competitions_permission_roles_exclusive",
                "NOT (manager_ids && judge_ids) AND NOT (manager_ids && observer_ids) AND NOT (judge_ids && observer_ids)");
            table.HasCheckConstraint(
                "ck_competitions_owner_not_permission",
                "NOT (owner_id = ANY(manager_ids)) AND NOT (owner_id = ANY(judge_ids)) AND NOT (owner_id = ANY(observer_ids))");
            table.HasCheckConstraint(
                "ck_competitions_flag_secret_length",
                "octet_length(flag_derivation_secret) = 32");
            table.HasCheckConstraint(
                "ck_competitions_question_limits",
                "max_active_questions_per_team > 0 AND max_participant_messages_before_handler_reply > 0");
            table.HasCheckConstraint(
                "ck_competitions_leaderboard_visibility_state",
                "leaderboard_visibility BETWEEN 0 AND 2 AND "
                + "((leaderboard_visibility = 0 AND leaderboard_visibility_starts_at IS NULL AND frozen_leaderboard_snapshot_json IS NULL) "
                + "OR (leaderboard_visibility = 1 AND leaderboard_visibility_starts_at IS NOT NULL "
                + "AND ((leaderboard_visibility_applied_at IS NULL AND frozen_leaderboard_snapshot_json IS NULL) "
                + "OR (leaderboard_visibility_applied_at IS NOT NULL AND frozen_leaderboard_snapshot_json IS NOT NULL))) "
                + "OR (leaderboard_visibility = 2 AND leaderboard_visibility_starts_at IS NOT NULL AND frozen_leaderboard_snapshot_json IS NULL))");
        });
    }
}
