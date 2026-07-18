using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Competitions;

internal sealed class CompetitionCollaboratorConfiguration : IEntityTypeConfiguration<CompetitionCollaborator>
{
    public void Configure(EntityTypeBuilder<CompetitionCollaborator> builder)
    {
        builder.ToTable("competition_collaborators");
        builder.HasKey(collaborator => collaborator.Id);
        builder.HasIndex(collaborator => new { collaborator.CompetitionId, collaborator.UserId }).IsUnique();
    }
}
