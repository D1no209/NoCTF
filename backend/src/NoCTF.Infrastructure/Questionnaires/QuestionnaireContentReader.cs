using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Questionnaires;

internal static class QuestionnaireContentReader
{
    public static async Task<Dictionary<Guid, QuestionnaireQuestion>> ReadAsync(NoCtfDbContext db, Guid[] versions, CancellationToken ct)
    {
        var questions = await db.Set<QuestionnaireQuestion>().AsNoTracking().IgnoreAutoIncludes()
            .Include(question => question.Revision).Include(question => question.Options)
            .Include(question => question.Classifications).Include(question => question.Files)
            .Where(question => versions.Contains(question.VersionId)).AsSplitQuery().ToArrayAsync(ct);
        return questions.ToDictionary(question => question.VersionId);
    }
}
