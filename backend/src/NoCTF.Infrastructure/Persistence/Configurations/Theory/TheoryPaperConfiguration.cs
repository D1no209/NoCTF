using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Theory;

internal sealed class TheoryPaperConfiguration : IEntityTypeConfiguration<TheoryParticipant>,
    IEntityTypeConfiguration<TheoryPaper>, IEntityTypeConfiguration<TheoryActivePaper>,
    IEntityTypeConfiguration<TheoryPaperItem>, IEntityTypeConfiguration<TheoryPaperOption>,
    IEntityTypeConfiguration<TheoryDrawReceipt>, IEntityTypeConfiguration<TheoryAnswerReceipt>, IEntityTypeConfiguration<TheoryIssueReceipt>
{
    public void Configure(EntityTypeBuilder<TheoryParticipant> builder)
    {
        builder.ToTable("theory_participants");
        builder.HasIndex(participant => new { participant.ActivityId, participant.UserId }).IsUnique();
        builder.HasIndex(participant => new { participant.ActivityId, participant.TeamId }).IsUnique();
        builder.HasOne<TheoryActivity>().WithMany().HasForeignKey(participant => participant.ActivityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(participant => participant.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(participant => participant.TeamId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<TheoryPaper> builder)
    {
        builder.ToTable("theory_papers");
        builder.HasIndex(paper => new { paper.ParticipantId, paper.Attempt }).IsUnique();
        builder.HasAlternateKey(paper => new { paper.ParticipantId, paper.Id });
        builder.HasOne<TheoryParticipant>().WithMany().HasForeignKey(paper => paper.ParticipantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(paper => paper.SubmittedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(paper => paper.Items).WithOne().HasForeignKey(item => item.PaperId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(paper => new { paper.State, paper.OpenedAt });
    }

    public void Configure(EntityTypeBuilder<TheoryActivePaper> builder)
    {
        builder.ToTable("theory_active_papers");
        builder.HasKey(slot => slot.ParticipantId);
        builder.HasIndex(slot => slot.PaperId).IsUnique();
        builder.HasOne<TheoryPaper>().WithOne().HasForeignKey<TheoryActivePaper>(slot => new { slot.ParticipantId, slot.PaperId })
            .HasPrincipalKey<TheoryPaper>(paper => new { paper.ParticipantId, paper.Id }).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<TheoryPaperItem> builder)
    {
        builder.ToTable("theory_paper_items");
        builder.HasIndex(item => new { item.PaperId, item.Order }).IsUnique();
        builder.HasIndex(item => new { item.PaperId, item.RevisionId }).IsUnique();
        builder.HasOne<TheoryQuestionRevision>().WithMany().HasForeignKey(item => item.RevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TheoryDrawRule>().WithMany().HasForeignKey(item => item.RuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.AnsweredById).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(item => item.Options).WithOne().HasForeignKey(option => option.ItemId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<TheoryPaperOption> builder)
    {
        builder.ToTable("theory_paper_options");
        builder.HasKey(option => new { option.ItemId, option.OptionId });
        builder.HasIndex(option => new { option.ItemId, option.Order }).IsUnique();
        builder.HasOne<TheoryQuestionOption>().WithMany().HasForeignKey(option => option.OptionId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<TheoryDrawReceipt> builder)
    {
        builder.ToTable("theory_draw_receipts");
        builder.HasKey(receipt => new { receipt.ParticipantId, receipt.SourceId });
        builder.HasOne<TheoryPaper>().WithMany().HasForeignKey(receipt => new { receipt.ParticipantId, receipt.PaperId })
            .HasPrincipalKey(paper => new { paper.ParticipantId, paper.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TheoryQuestionSource>().WithMany().HasForeignKey(receipt => receipt.SourceId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<TheoryAnswerReceipt> builder)
    {
        builder.ToTable("theory_answer_receipts");
        builder.HasKey(receipt => new { receipt.ItemId, receipt.RequestId });
        builder.Property(receipt => receipt.SelectionDigest).HasMaxLength(32).IsRequired();
        builder.HasOne<TheoryPaperItem>().WithMany().HasForeignKey(receipt => receipt.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(receipt => receipt.ActorId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<TheoryIssueReceipt> builder)
    {
        builder.ToTable("theory_issue_receipts");
        builder.HasKey(receipt => new { receipt.ParticipantId, receipt.RequestId });
        builder.HasOne<TheoryPaper>().WithMany().HasForeignKey(receipt => new { receipt.ParticipantId, receipt.PaperId })
            .HasPrincipalKey(paper => new { paper.ParticipantId, paper.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
