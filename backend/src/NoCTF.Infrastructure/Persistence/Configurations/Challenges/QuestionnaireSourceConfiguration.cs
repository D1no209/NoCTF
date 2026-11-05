using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Infrastructure.Persistence.Configurations.Challenges;

internal sealed class QuestionnaireSourceConfiguration : IEntityTypeConfiguration<QuestionnaireQuestionSource>
{
    public void Configure(EntityTypeBuilder<QuestionnaireQuestionSource> builder) => builder.ToTable("questionnaire_sources");
}
