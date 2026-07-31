using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class TeamModerationTests
{
    [Test]
    public async Task ExecuteAsync_FinishedCompetition_DoesNotMutate()
    {
        var store = new Store { Status = CompetitionStatus.Finished };
        var useCase = new ModerateTeam(store);

        var result = await useCase.ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true, "reason", DateTimeOffset.UtcNow));

        await Assert.That(result.ErrorCode).IsEqualTo("competition_finished");
        await Assert.That(store.ApplyCalls).IsEqualTo(0);
    }

    private sealed class Store : ITeamModerationStore
    {
        public CompetitionStatus? Status { get; init; }
        public int ApplyCalls { get; private set; }
        public Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(Status);
        public Task<TeamModerationStoreResult> ApplyAsync(TeamModerationCommand command, CancellationToken cancellationToken)
        {
            ApplyCalls++;
            return Task.FromResult(new TeamModerationStoreResult());
        }
    }

}
