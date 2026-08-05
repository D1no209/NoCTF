using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.Configurations.Challenges;

internal sealed class CompetitionQuestionConfiguration
    : IEntityTypeConfiguration<CompetitionQuestion>
{
    public void Configure(EntityTypeBuilder<CompetitionQuestion> builder)
    {
        builder.ToTable("competition_questions", table =>
        {
            table.HasCheckConstraint(
                "ck_competition_questions_subject_scope",
                "(subject = 0 AND competition_challenge_id IS NOT NULL) OR "
                + "(subject = 1 AND competition_challenge_id IS NULL)");
            table.HasCheckConstraint(
                "ck_competition_questions_revision",
                "revision >= 0");
        });
        builder.HasKey(question => question.Id);
        builder.Property(question => question.Subject).HasConversion<short>();
        builder.Property(question => question.Status).HasConversion<short>();
        builder.HasIndex(question => new
        {
            question.CompetitionId,
            question.UpdatedAt,
            question.Id
        });
        builder.HasIndex(question => new
        {
            question.CompetitionChallengeId,
            question.Status,
            question.UpdatedAt,
            question.Id
        });
        builder.HasIndex(question => new
        {
            question.TeamId,
            question.CreatedAt,
            question.Id
        });
        builder.HasIndex(question => new
        {
            question.CompetitionId,
            question.PublishedAt,
            question.Id
        });
        builder.HasOne<Competition>().WithMany()
            .HasForeignKey(question => question.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompetitionChallenge>().WithMany()
            .HasForeignKey(question => question.CompetitionChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany()
            .HasForeignKey(question => question.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(question => question.AskedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Submission>().WithMany()
            .HasForeignKey(question => question.SubmissionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(question => question.PublishedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(question => question.Entries)
            .WithOne()
            .HasForeignKey(entry => entry.QuestionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class CompetitionQuestionEntryConfiguration
    : IEntityTypeConfiguration<CompetitionQuestionEntry>
{
    public void Configure(EntityTypeBuilder<CompetitionQuestionEntry> builder)
    {
        builder.ToTable("competition_question_entries", table =>
        {
            table.HasCheckConstraint(
                "ck_competition_question_entries_shape",
                "(kind = 0 AND body IS NOT NULL AND from_status IS NULL AND to_status IS NULL) OR "
                + "(kind = 1 AND body IS NULL AND from_status IS NOT NULL AND to_status IS NOT NULL) OR "
                + "(kind = 2 AND body IS NULL AND target_entry_id IS NOT NULL)");
        });
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Kind).HasConversion<short>();
        builder.Property(entry => entry.ActorRole).HasConversion<short>();
        builder.Property(entry => entry.FromStatus).HasConversion<short>();
        builder.Property(entry => entry.ToStatus).HasConversion<short>();
        builder.HasIndex(entry => new { entry.QuestionId, entry.CreatedAt, entry.Id });
        builder.HasOne<User>().WithMany()
            .HasForeignKey(entry => entry.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompetitionQuestionEntry>().WithMany()
            .HasForeignKey(entry => entry.TargetEntryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(entry => entry.PublishedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
