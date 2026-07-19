using NoCTF.Application.Competitions.Collaborators;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class CompetitionCollaboratorTests
{
    [Test]
    public async Task AddCollaborator_PropagatesStoreConflict()
    {
        var store = new Store { Error = "owner_is_not_collaborator" };
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

    private sealed class Store : ICompetitionCollaboratorStore
    {
        public string? Error { get; set; }
        public Task<bool> CanManageAsync(Guid actorId, Guid competitionId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<CompetitionCollaboratorView>> ListAsync(Guid competitionId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CompetitionCollaboratorView>>([]);
        public Task<string?> AddOrUpdateAsync(AddCompetitionCollaboratorCommand command, CancellationToken cancellationToken) => Task.FromResult(Error);
        public Task<bool> RemoveAsync(Guid competitionId, Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
