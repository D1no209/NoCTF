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
            now.AddMinutes(1), now.AddHours(2), true, 5, 2, Guid.NewGuid(), now,
            AccessMode: CompetitionAccessMode.StaffOnly);

        var result = await new CreateCompetition(store).ExecuteAsync(command);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.Last!.Status).IsEqualTo(CompetitionStatus.Draft);
        await Assert.That(store.Last.Mode).IsEqualTo(GameMode.Ctf);
        await Assert.That(store.Last.MaxConcurrentRuntimeInstancesPerTeam).IsEqualTo(2);
        await Assert.That(store.Last.AccessMode).IsEqualTo(CompetitionAccessMode.StaffOnly);
    }

    [Test]
    public async Task CreateCompetition_RejectsInvalidScheduleAndTeamSize()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new Store();
        var invalidSchedule = await new CreateCompetition(store).ExecuteAsync(new(
            "CTF", null, GameMode.Ctf, now, now, true, 5, 0, Guid.NewGuid(), now));
        var invalidSize = await new CreateCompetition(store).ExecuteAsync(new(
            "CTF", null, GameMode.Ctf, now, now.AddHours(1), true, 0, 0, Guid.NewGuid(), now));

        await Assert.That(invalidSchedule.State)
            .IsEqualTo(CompetitionCreationState.InvalidRequest);
        await Assert.That(invalidSchedule.Detail)
            .IsEqualTo("Competition start time must be before its end time.");
        await Assert.That(invalidSize.State)
            .IsEqualTo(CompetitionCreationState.InvalidRequest);
        await Assert.That(invalidSize.Detail)
            .IsEqualTo("MaxTeamMembers must be greater than zero.");
    }

    [Test]
    public async Task Practice_mode_is_available_only_for_ctf_competitions()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new Store();

        var rejected = await new CreateCompetition(store).ExecuteAsync(new(
            "AWD practice", null, GameMode.Awd, now, now.AddHours(1), true, 5, 1,
            Guid.NewGuid(), now, PracticeModeEnabled: true));
        var accepted = await new CreateCompetition(store).ExecuteAsync(new(
            "CTF practice", null, GameMode.Ctf, now, now.AddHours(1), true, 5, 1,
            Guid.NewGuid(), now, PracticeModeEnabled: true));

        await Assert.That(rejected.State)
            .IsEqualTo(CompetitionCreationState.InvalidRequest);
        await Assert.That(rejected.Detail)
            .IsEqualTo("Practice mode is supported only for CTF competitions.");
        await Assert.That(accepted.Succeeded).IsTrue();
        await Assert.That(store.Last!.PracticeModeEnabled).IsTrue();
    }

    [Test]
    [Arguments(CompetitionStatus.Running)]
    [Arguments(CompetitionStatus.Paused)]
    public async Task UpdateCompetition_ActiveCompetitionAllowsConfigurationChanges(CompetitionStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var store = new Store { Last = new(Guid.NewGuid(), "CTF", null, GameMode.Ctf, now, now.AddHours(2), status, true, 5, 0, Guid.NewGuid()) };
        var result = await new UpdateCompetition(store).ExecuteAsync(new(store.Last.Id, "CTF", null,
            now, now.AddHours(3), true, 5, 0, Guid.NewGuid(), now));

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task UpdateCompetition_RunningAllowsDisplayMetadataOnly()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new Store { Last = new(Guid.NewGuid(), "CTF", null, GameMode.Ctf, now, now.AddHours(2), CompetitionStatus.Running, true, 5, 0, Guid.NewGuid()) };

        var result = await new UpdateCompetition(store).ExecuteAsync(new(store.Last.Id, "Renamed", "Public details",
            store.Last.StartTime, store.Last.EndTime, store.Last.TeamRegistrationAutoApprove,
            store.Last.MaxTeamMembers, store.Last.MaxConcurrentRuntimeInstancesPerTeam,
            Guid.NewGuid(), now));

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task CompetitionManagementPolicy_AllowsOperationalFieldsWhileRunning()
    {
        var now = DateTimeOffset.UtcNow;
        var current = new CompetitionView(Guid.NewGuid(), "CTF", null, GameMode.Ctf, now,
            now.AddHours(2), CompetitionStatus.Running, true, 5, 0, Guid.NewGuid());
        var baseline = new UpdateCompetitionCommand(current.Id, current.Title, current.Description,
            current.StartTime, current.EndTime, current.TeamRegistrationAutoApprove,
            current.MaxTeamMembers, current.MaxConcurrentRuntimeInstancesPerTeam,
            Guid.NewGuid(), now);
        UpdateCompetitionCommand[] mutations =
        [
            baseline with { StartTime = baseline.StartTime.AddMinutes(1) },
            baseline with { EndTime = baseline.EndTime.AddMinutes(1) },
            baseline with { TeamRegistrationAutoApprove = !baseline.TeamRegistrationAutoApprove },
            baseline with { MaxTeamMembers = baseline.MaxTeamMembers + 1 },
            baseline with
            {
                MaxConcurrentRuntimeInstancesPerTeam =
                    baseline.MaxConcurrentRuntimeInstancesPerTeam + 1
            }
        ];

        foreach (var mutation in mutations)
            await Assert.That(CompetitionManagementPolicy.ValidateUpdate(current, mutation).Succeeded)
                .IsTrue();
    }

    [Test]
    public async Task FinishedCompetition_AllowsUpdateButRejectsDelete()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new Store { Last = new(Guid.NewGuid(), "CTF", null, GameMode.Ctf, now, now.AddHours(2), CompetitionStatus.Finished, true, 5, 0, Guid.NewGuid()) };

        var update = await new UpdateCompetition(store).ExecuteAsync(new(store.Last.Id, "Renamed", null,
            store.Last.StartTime, store.Last.EndTime, true, 5, 0, Guid.NewGuid(), now));
        var delete = await new DeleteCompetition(store).ExecuteAsync(store.Last.Id, Guid.NewGuid(), now);

        await Assert.That(update.Succeeded).IsTrue();
        await Assert.That(delete.FailureCode).IsEqualTo(CompetitionManagementFailureCode.CompetitionFinished);
    }

    [Test]
    public async Task UpdateCompetition_rejects_WriteUp_deadlines_outside_the_supported_range()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new Store
        {
            Last = new(Guid.NewGuid(), "CTF", null, GameMode.Ctf, now,
                now.AddHours(2), CompetitionStatus.Draft, true, 5, 0, Guid.NewGuid())
        };

        var result = await new UpdateCompetition(store).ExecuteAsync(new(
            store.Last.Id,
            store.Last.Title,
            store.Last.Description,
            store.Last.StartTime,
            store.Last.EndTime,
            store.Last.TeamRegistrationAutoApprove,
            store.Last.MaxTeamMembers,
            store.Last.MaxConcurrentRuntimeInstancesPerTeam,
            Guid.NewGuid(),
            now,
            WriteUpSubmissionRequired: true,
            WriteUpSubmissionDeadlineHours:
                CompetitionWriteUpPolicy.MaximumDeadlineHours + 1));

        await Assert.That(result.FailureCode)
            .IsEqualTo(CompetitionManagementFailureCode.CompetitionConflict);
    }

    [Test]
    public async Task DeleteCompetition_RejectsActiveCompetition()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new Store { Last = new(Guid.NewGuid(), "CTF", null, GameMode.Ctf, now, now.AddHours(2), CompetitionStatus.Running, true, 5, 0, Guid.NewGuid()) };

        var result = await new DeleteCompetition(store).ExecuteAsync(store.Last.Id, Guid.NewGuid(), now);

        await Assert.That(result.FailureCode).IsEqualTo(CompetitionManagementFailureCode.CompetitionActive);
    }

    private sealed class Store : ICompetitionManagementStore
    {
        public CompetitionView? Last { get; set; }
        public Task<CompetitionCreationResult> CreateAsync(
            CreateCompetitionCommand command,
            CancellationToken cancellationToken)
        {
            Last = new(Guid.NewGuid(), command.Title, command.Description, command.Mode, command.StartTime, command.EndTime,
                CompetitionStatus.Draft, command.TeamRegistrationAutoApprove, command.MaxTeamMembers,
                command.MaxConcurrentRuntimeInstancesPerTeam, command.OwnerId,
                PracticeModeEnabled: command.PracticeModeEnabled,
                AccessMode: command.AccessMode);
            return Task.FromResult(new CompetitionCreationResult(
                CompetitionCreationState.Created,
                Last));
        }
        public Task<CompetitionView?> FindAsync(Guid competitionId, bool includeDraft, CancellationToken cancellationToken) => Task.FromResult(Last);
        public Task<IReadOnlyList<CompetitionView>> ListAsync(bool includeDraft, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CompetitionView>>(Last is null ? [] : [Last]);
        public Task<CompetitionView?> UpdateAsync(
            UpdateCompetitionCommand command,
            CancellationToken cancellationToken) => Task.FromResult(Last);
        public Task<bool> SoftDeleteAsync(Guid competitionId, CompetitionStatus expectedStatus, Guid actorId, DateTimeOffset deletedAt, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
