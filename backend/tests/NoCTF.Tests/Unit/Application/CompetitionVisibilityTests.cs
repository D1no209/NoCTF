using NoCTF.Application.Competitions.Visibility;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public sealed class CompetitionVisibilityTests
{
    [Test]
    public async Task Effective_visibility_honors_schedule_and_finished_reveal()
    {
        var now = DateTimeOffset.UtcNow;
        await Assert.That(CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            CompetitionStatus.Running,
            CompetitionLeaderboardVisibility.Frozen,
            now.AddMinutes(1),
            now)).IsEqualTo(CompetitionLeaderboardVisibility.Normal);
        await Assert.That(CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            CompetitionStatus.Running,
            CompetitionLeaderboardVisibility.Frozen,
            now,
            now)).IsEqualTo(CompetitionLeaderboardVisibility.Frozen);
        await Assert.That(CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            CompetitionStatus.Finished,
            CompetitionLeaderboardVisibility.Blackout,
            now.AddMinutes(-1),
            now)).IsEqualTo(CompetitionLeaderboardVisibility.Normal);
    }

    [Test]
    public async Task Scheduled_visibility_must_be_future_and_inside_competition_window()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new RecordingStore(View(now));
        var useCase = new UpdateCompetitionVisibility(store);

        var past = await useCase.ExecuteAsync(Command(now, now.AddSeconds(-1)));
        var beforeCompetition = await useCase.ExecuteAsync(Command(now, now.AddMinutes(5)));
        var afterCompetition = await useCase.ExecuteAsync(Command(now, now.AddHours(3)));

        await Assert.That(past.State).IsEqualTo(CompetitionVisibilityMutationState.InvalidSchedule);
        await Assert.That(beforeCompetition.State).IsEqualTo(CompetitionVisibilityMutationState.InvalidSchedule);
        await Assert.That(afterCompetition.State).IsEqualTo(CompetitionVisibilityMutationState.InvalidSchedule);
        await Assert.That(store.Updates).IsEmpty();
    }

    [Test]
    public async Task Immediate_restriction_uses_null_schedule_and_reaches_store()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new RecordingStore(View(now));
        var useCase = new UpdateCompetitionVisibility(store);

        var result = await useCase.ExecuteAsync(Command(now, startsAt: null));

        await Assert.That(result.State).IsEqualTo(CompetitionVisibilityMutationState.Updated);
        await Assert.That(store.Updates).HasSingleItem();
        await Assert.That(store.Updates[0].StartsAt).IsNull();
    }

    [Test]
    public async Task Finished_competition_only_accepts_normal_visibility()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new RecordingStore(View(now) with
        {
            CompetitionStatus = CompetitionStatus.Finished
        });

        var result = await new UpdateCompetitionVisibility(store)
            .ExecuteAsync(Command(now, startsAt: null));

        await Assert.That(result.State)
            .IsEqualTo(CompetitionVisibilityMutationState.CompetitionFinished);
        await Assert.That(store.Updates).IsEmpty();
    }

    private static CompetitionVisibilityConfigurationView View(DateTimeOffset now) =>
        new(
            Guid.CreateVersion7(),
            CompetitionStatus.Running,
            now.AddMinutes(10),
            now.AddHours(2),
            CompetitionLeaderboardVisibility.Normal,
            CompetitionLeaderboardVisibility.Normal,
            null,
            null,
            3);

    private static UpdateCompetitionVisibilityCommand Command(
        DateTimeOffset now,
        DateTimeOffset? startsAt) =>
        new(
            Guid.CreateVersion7(),
            CompetitionLeaderboardVisibility.Frozen,
            startsAt,
            3,
            Guid.CreateVersion7(),
            null,
            now);

    private sealed class RecordingStore(CompetitionVisibilityConfigurationView view)
        : ICompetitionVisibilityStore
    {
        public List<UpdateCompetitionVisibilityCommand> Updates { get; } = [];

        public Task<CompetitionVisibilityConfigurationView?> GetAsync(
            Guid competitionId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionVisibilityConfigurationView?>(view);

        public Task<CompetitionVisibilityMutationResult> UpdateAsync(
            UpdateCompetitionVisibilityCommand command,
            CancellationToken cancellationToken)
        {
            Updates.Add(command);
            return Task.FromResult(new CompetitionVisibilityMutationResult(
                CompetitionVisibilityMutationState.Updated,
                view));
        }

        public Task ApplyScheduledAsync(
            Guid competitionId,
            int expectedRevision,
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
