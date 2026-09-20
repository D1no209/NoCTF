using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Competitions;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace NoCTF.Infrastructure.Persistence.Configurations.Competitions;

internal sealed class CompetitionEntityConfiguration : IEntityTypeConfiguration<Competition>
{
    private static readonly JsonSerializerOptions WebhookJsonOptions =
        new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Competition> builder)
    {
        builder.ToTable("competitions");
        builder.HasKey(competition => competition.Id);
        builder.Property(competition => competition.Title).HasMaxLength(160);
        builder.Property(competition => competition.ConfigurationJson).HasColumnType("jsonb");
        builder.Property(competition => competition.WebhookConfiguration)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{\"schemaVersion\":1,\"targets\":[]}'::jsonb")
            .HasConversion(
                value => JsonSerializer.Serialize(value, WebhookJsonOptions),
                value => JsonSerializer.Deserialize<CompetitionWebhookConfiguration>(
                        value,
                        WebhookJsonOptions)
                    ?? new CompetitionWebhookConfiguration(),
                new ValueComparer<CompetitionWebhookConfiguration>(
                    (left, right) => SerializeWebhook(left) == SerializeWebhook(right),
                    value => SerializeWebhook(value).GetHashCode(StringComparison.Ordinal),
                    value => DeserializeWebhook(SerializeWebhook(value))));
        builder.Property(competition => competition.TrackConfigurationJson)
            .HasColumnType("jsonb");
        builder.Property(competition => competition.ManagerIds).HasColumnType("uuid[]");
        builder.Property(competition => competition.JudgeIds).HasColumnType("uuid[]");
        builder.Property(competition => competition.ObserverIds).HasColumnType("uuid[]");
        builder.Property(competition => competition.Mode).HasConversion<short>();
        builder.Property(competition => competition.Status).HasConversion<short>();
        // Existing competitions used tracks implicitly, so the schema default backfills true.
        // ValueGeneratedNever makes application inserts persist the explicit new default false.
        builder.Property(competition => competition.TracksEnabled)
            .HasDefaultValue(true)
            .ValueGeneratedNever();
        // Schema defaults preserve the documented cross-mode question policy when an existing
        // competition row is upgraded; Data Annotations cannot express database defaults.
        builder.Property(competition => competition.MaxActiveQuestionsPerTeam).HasDefaultValue(5);
        builder.Property(competition => competition.MaxParticipantMessagesBeforeHandlerReply).HasDefaultValue(3);
        builder.Property(competition => competition.AllowChallengeOwnersToHandleQuestions).HasDefaultValue(true);
        builder.Property(competition => competition.WriteUpSubmissionRequired).HasDefaultValue(false);
        builder.Property(competition => competition.WriteUpSubmissionDeadlineHours).HasDefaultValue(0);
        builder.HasQueryFilter(competition => competition.DeletedAt == null);
        builder.HasIndex(competition => new { competition.Status, competition.StartAt });
        builder.HasIndex(competition => new { competition.FrozenStartAt, competition.HiddenStartAt });
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
                "ck_competitions_write_up_submission_deadline_hours",
                $"write_up_submission_deadline_hours BETWEEN 0 AND {CompetitionWriteUpPolicy.MaximumDeadlineHours}");
            table.HasCheckConstraint(
                "ck_competitions_webhook_configuration",
                "jsonb_typeof(webhook_configuration) = 'object'"
                + " AND (webhook_configuration ->> 'schemaVersion')::integer = 1"
                + " AND jsonb_typeof(webhook_configuration -> 'targets') = 'array'");
        });
    }

    private static string SerializeWebhook(CompetitionWebhookConfiguration? value) =>
        JsonSerializer.Serialize(value ?? new CompetitionWebhookConfiguration(), WebhookJsonOptions);

    private static CompetitionWebhookConfiguration DeserializeWebhook(string value) =>
        JsonSerializer.Deserialize<CompetitionWebhookConfiguration>(value, WebhookJsonOptions)
        ?? new CompetitionWebhookConfiguration();
}
