using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Domain.Questionnaires.Sessions;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Infrastructure.Persistence.Configurations.Questionnaires;

internal sealed class QuestionnaireSessionConfiguration : IEntityTypeConfiguration<QuestionnaireParticipant>,
    IEntityTypeConfiguration<QuestionnaireSession>, IEntityTypeConfiguration<QuestionnaireActiveSession>,
    IEntityTypeConfiguration<QuestionnaireSessionQuestion>, IEntityTypeConfiguration<QuestionnaireSessionOption>,
    IEntityTypeConfiguration<QuestionnaireDrawReceipt>, IEntityTypeConfiguration<QuestionnaireAnswerReceipt>, IEntityTypeConfiguration<QuestionnaireIssueReceipt>
{
    public void Configure(EntityTypeBuilder<QuestionnaireParticipant> builder)
    {
        builder.ToTable("questionnaire_participants");
        builder.HasIndex(participant => new { participant.ActivityId, participant.UserId }).IsUnique();
        builder.HasOne<QuestionnaireActivity>().WithMany().HasForeignKey(participant => participant.ActivityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(participant => participant.UserId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<QuestionnaireSession> builder)
    {
        builder.ToTable("questionnaire_sessions");
        builder.HasIndex(session => new { session.ParticipantId, session.Attempt }).IsUnique();
        builder.HasAlternateKey(session => new { session.ParticipantId, session.Id });
        builder.HasOne<QuestionnaireParticipant>().WithMany().HasForeignKey(session => session.ParticipantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(session => session.SubmittedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(session => session.Items).WithOne().HasForeignKey(item => item.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(session => new { session.State, session.OpenedAt });
    }

    public void Configure(EntityTypeBuilder<QuestionnaireActiveSession> builder)
    {
        builder.ToTable("questionnaire_active_sessions");
        builder.HasKey(slot => slot.ParticipantId);
        builder.HasIndex(slot => slot.SessionId).IsUnique();
        builder.HasOne<QuestionnaireSession>().WithOne().HasForeignKey<QuestionnaireActiveSession>(slot => new { slot.ParticipantId, slot.SessionId })
            .HasPrincipalKey<QuestionnaireSession>(session => new { session.ParticipantId, session.Id }).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<QuestionnaireSessionQuestion> builder)
    {
        builder.ToTable("questionnaire_session_questions");
        builder.HasIndex(item => new { item.SessionId, item.Order }).IsUnique();
        builder.HasIndex(item => new { item.SessionId, item.RevisionId }).IsUnique();
        builder.HasAlternateKey(item => new { item.Id, item.RevisionId });
        builder.HasOne<QuestionnaireQuestion>().WithMany()
            .HasForeignKey(item => new { item.RevisionId, item.QuestionnaireRevisionId, item.QuestionnaireQuestionId })
            .HasPrincipalKey(question => new { question.VersionId, question.RevisionId, question.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QuestionnaireAssignment>().WithMany()
            .HasForeignKey(item => new { item.AssignmentId, item.SessionId, item.QuestionnaireRevisionId })
            .HasPrincipalKey(assignment => new { assignment.Id, assignment.SessionId, assignment.RevisionId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QuestionnaireDrawRule>().WithMany().HasForeignKey(item => item.RuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.AnsweredById).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(item => item.Options).WithOne().HasForeignKey(option => new { option.ItemId, option.QuestionVersionId })
            .HasPrincipalKey(item => new { item.Id, item.RevisionId }).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<QuestionnaireSessionOption> builder)
    {
        builder.ToTable("questionnaire_session_options");
        builder.HasKey(option => new { option.ItemId, option.OptionId });
        builder.HasIndex(option => new { option.ItemId, option.Order }).IsUnique();
        builder.HasOne<QuestionnaireOption>().WithMany().HasForeignKey(option => new { option.QuestionVersionId, option.OptionId }).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<QuestionnaireDrawReceipt> builder)
    {
        builder.ToTable("questionnaire_draw_receipts");
        builder.HasKey(receipt => new { receipt.ParticipantId, receipt.SourceId });
        builder.HasOne<QuestionnaireSession>().WithMany().HasForeignKey(receipt => new { receipt.ParticipantId, receipt.SessionId })
            .HasPrincipalKey(session => new { session.ParticipantId, session.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QuestionnaireQuestionSource>().WithMany().HasForeignKey(receipt => receipt.SourceId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<QuestionnaireAnswerReceipt> builder)
    {
        builder.ToTable("questionnaire_answer_receipts");
        builder.HasKey(receipt => new { receipt.ItemId, receipt.RequestId });
        builder.Property(receipt => receipt.SelectionDigest).HasMaxLength(32).IsRequired();
        builder.HasOne<QuestionnaireSessionQuestion>().WithMany().HasForeignKey(receipt => receipt.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(receipt => receipt.ActorId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<QuestionnaireIssueReceipt> builder)
    {
        builder.ToTable("questionnaire_issue_receipts");
        builder.HasKey(receipt => new { receipt.ParticipantId, receipt.RequestId });
        builder.HasOne<QuestionnaireSession>().WithMany().HasForeignKey(receipt => new { receipt.ParticipantId, receipt.SessionId })
            .HasPrincipalKey(session => new { session.ParticipantId, session.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
