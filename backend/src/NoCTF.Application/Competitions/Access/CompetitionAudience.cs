using NoCTF.Domain.Competitions;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.Application.Competitions.Access;

public interface ICompetitionAudienceReader
{
    Task<CompetitionAccessMode?> GetAccessModeAsync(
        Guid competitionId,
        CancellationToken cancellationToken);
}

public static class CompetitionAudiencePolicy
{
    public static async Task<IReadOnlyList<CompetitionView>> FilterCatalogAsync(
        IReadOnlyList<CompetitionView> items,
        Guid userId,
        bool isAdministrator,
        ICompetitionModerationAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        var visible = new List<CompetitionView>(items.Count);
        foreach (var item in items)
        {
            if (item.AccessMode == CompetitionAccessMode.Public
                || userId != Guid.Empty
                && (isAdministrator
                    || await authorizer.CanObserveAsync(
                        userId,
                        item.Id,
                        cancellationToken)))
            {
                visible.Add(item);
            }
        }
        return visible;
    }
}
