using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class CompetitionManagementTests
{
    [Test]
    public async Task CreateCompetition_ValidDraftIsPersistedThroughStore()
    {
        var store = new Store();
        var now = DateTimeOffset.UtcNow;
        var command = new CreateCompetitionCommand("Spring CTF", "desc", GameMode.Ctf,
            now.AddMinutes(1), now.AddHours(2), true, 5, Guid.NewGuid(), now);

        var result = await new CreateCompetition(store).ExecuteAsync(command);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.Last!.Status).IsEqualTo(CompetitionStatus.Draft);
        await Assert.That(store.Last.Mode).IsEqualTo(GameMode.Ctf);
    }

    [Test]
    public async Task CreateCompetition_RejectsInvalidScheduleAndTeamSize()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new Store();
        var invalidSchedule = await new CreateCompetition(store).ExecuteAsync(new(
            "CTF", null, GameMode.Ctf, now, now, true, 5, Guid.NewGuid(), now));
        var invalidSize = await new CreateCompetition(store).ExecuteAsync(new(
            "CTF", null, GameMode.Ctf, now, now.AddHours(1), true, 0, Guid.NewGuid(), now));

        await Assert.That(invalidSchedule.ErrorCode).IsEqualTo("invalid_schedule");
        await Assert.That(invalidSize.ErrorCode).IsEqualTo("invalid_team_size");
    }

    private sealed class Store : ICompetitionManagementStore
    {
        public CompetitionView? Last { get; private set; }
        public Task<CompetitionView> CreateAsync(CreateCompetitionCommand command, CancellationToken cancellationToken)
        {
            Last = new(Guid.NewGuid(), command.Title, command.Description, command.Mode, command.StartTime, command.EndTime,
                CompetitionStatus.Draft, command.TeamRegistrationAutoApprove, command.MaxTeamMembers, command.OwnerId);
            return Task.FromResult(Last);
        }
        public Task<CompetitionView?> FindAsync(Guid competitionId, bool includeDraft, CancellationToken cancellationToken) => Task.FromResult(Last);
        public Task<IReadOnlyList<CompetitionView>> ListAsync(bool includeDraft, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CompetitionView>>(Last is null ? [] : [Last]);
    }
}
