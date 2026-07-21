using NoCTF.Application.Competitions.Collaborators;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class CompetitionCollaboratorTests
{
    [Test]
    public async Task AddCollaborator_PropagatesStoreConflict()
    {
        var store = new Store { Failure = CompetitionCollaboratorFailure.OwnerIsNotCollaborator };
        var result = await new AddCompetitionCollaborator(store).ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), CompetitionCollaboratorRole.Manager, DateTimeOffset.UtcNow));
        await Assert.That(result.ErrorCode).IsEqualTo("owner_is_not_collaborator");
    }

    [Test]
    public async Task Roles_IncludeManagerJudgeAndObserver()
    {
        await Assert.That(Enum.GetValues<CompetitionCollaboratorRole>()).IsEquivalentTo([
            CompetitionCollaboratorRole.Manager, CompetitionCollaboratorRole.Judge, CompetitionCollaboratorRole.Observer]);
    }

    [Test]
    public async Task FinishedCompetition_RejectsCollaboratorMutationsBeforeStoreWrite()
    {
        var store = new Store { Status = CompetitionStatus.Finished };

        var add = await new AddCompetitionCollaborator(store).ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), CompetitionCollaboratorRole.Manager, DateTimeOffset.UtcNow));
        var remove = await new RemoveCompetitionCollaborator(store).ExecuteAsync(Guid.NewGuid(), Guid.NewGuid());

        await Assert.That(add.ErrorCode).IsEqualTo("competition_finished");
        await Assert.That(remove.ErrorCode).IsEqualTo("competition_finished");
        await Assert.That(store.WriteCount).IsEqualTo(0);
    }

    private sealed class Store : ICompetitionCollaboratorStore
    {
        public CompetitionCollaboratorFailure? Failure { get; set; }
        public CompetitionStatus? Status { get; set; } = CompetitionStatus.Draft;
        public int WriteCount { get; private set; }
        public Task<bool> CanManageAsync(Guid actorId, Guid competitionId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken cancellationToken) => Task.FromResult(Status);
        public Task<IReadOnlyList<CompetitionCollaboratorView>> ListAsync(Guid competitionId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CompetitionCollaboratorView>>([]);
        public Task<CompetitionCollaboratorFailure?> AddOrUpdateAsync(AddCompetitionCollaboratorCommand command, CancellationToken cancellationToken)
        {
            WriteCount++;
            return Task.FromResult(Failure);
        }
        public Task<CompetitionCollaboratorFailure?> RemoveAsync(Guid competitionId, Guid userId, CancellationToken cancellationToken)
        {
            WriteCount++;
            return Task.FromResult(Failure);
        }
    }
}
