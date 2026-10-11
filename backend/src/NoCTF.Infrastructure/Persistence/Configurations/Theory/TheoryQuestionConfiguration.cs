using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Theory.Questions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Theory;

internal sealed class TheoryQuestionConfiguration : IEntityTypeConfiguration<TheoryQuestion>,
    IEntityTypeConfiguration<TheoryQuestionSource>, IEntityTypeConfiguration<TheoryQuestionRevision>,
    IEntityTypeConfiguration<TheoryQuestionOption>, IEntityTypeConfiguration<TheoryQuestionGrant>,
    IEntityTypeConfiguration<TheoryQuestionClassification>, IEntityTypeConfiguration<TheoryQuestionFile>
{
    public void Configure(EntityTypeBuilder<TheoryQuestion> builder)
    {
        builder.ToTable("theory_questions");
        builder.HasOne<User>().WithMany().HasForeignKey(question => question.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TheoryQuestionSource>().WithMany().HasForeignKey(question => question.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(question => question.Revisions).WithOne().HasForeignKey(revision => revision.QuestionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(question => question.Grants).WithOne().HasForeignKey(grant => grant.QuestionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(question => new { question.OwnerId, question.State, question.UpdatedAt });
    }

    public void Configure(EntityTypeBuilder<TheoryQuestionSource> builder) => builder.ToTable("theory_question_sources");

    public void Configure(EntityTypeBuilder<TheoryQuestionRevision> builder)
    {
        builder.ToTable("theory_question_revisions");
        builder.HasIndex(revision => new { revision.QuestionId, revision.Version }).IsUnique();
        builder.HasMany(revision => revision.Options).WithOne().HasForeignKey(option => option.RevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(revision => revision.Classifications).WithOne().HasForeignKey(item => item.RevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(revision => revision.Files).WithOne().HasForeignKey(file => file.RevisionId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<TheoryQuestionOption> builder)
    {
        builder.ToTable("theory_question_options");
        builder.HasIndex(option => new { option.RevisionId, option.Order }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<TheoryQuestionGrant> builder)
    {
        builder.ToTable("theory_question_grants");
        builder.HasKey(grant => new { grant.QuestionId, grant.UserId });
        builder.HasOne<User>().WithMany().HasForeignKey(grant => grant.UserId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<TheoryQuestionClassification> builder)
    {
        builder.ToTable("theory_question_classifications");
        builder.HasKey(item => new { item.RevisionId, item.Kind, item.NormalizedName });
        builder.HasIndex(item => new { item.Kind, item.NormalizedName });
    }

    public void Configure(EntityTypeBuilder<TheoryQuestionFile> builder)
    {
        builder.ToTable("theory_question_files");
        builder.HasKey(file => new { file.RevisionId, file.FileId });
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(file => file.FileId).OnDelete(DeleteBehavior.Restrict);
    }
}
