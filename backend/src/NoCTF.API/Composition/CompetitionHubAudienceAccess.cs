using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Competitions.Access;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Composition;

public sealed class CompetitionHubAudienceAccess(IServiceScopeFactory scopes)
    : ICompetitionHubAudienceAccess
{
    public async Task<CompetitionAccessMode?> GetAccessModeAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICompetitionAudienceReader>()
            .GetAccessModeAsync(competitionId, cancellationToken);
    }

    public async Task<CompetitionHubAccessDecision?> ResolveAsync(
        Guid userId,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICompetitionHubAccess>()
            .ResolveAsync(userId, competitionId, cancellationToken);
    }
}
