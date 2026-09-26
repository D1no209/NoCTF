using NoCTF.Application.Competitions.Visibility;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public sealed class CompetitionVisibilityTests
{
    [Test]
    public async Task Effective_visibility_uses_only_effective_timestamps_and_latest_transition()
    {
        var now = DateTimeOffset.UtcNow;
        var cases = new[]
        {
            (Frozen: (DateTimeOffset?)null, Hidden: (DateTimeOffset?)null,
                Expected: CompetitionLeaderboardVisibility.Normal),
            (Frozen: (DateTimeOffset?)now.AddMinutes(1), Hidden: (DateTimeOffset?)null,
                Expected: CompetitionLeaderboardVisibility.Normal),
            (Frozen: (DateTimeOffset?)null, Hidden: (DateTimeOffset?)now.AddMinutes(1),
                Expected: CompetitionLeaderboardVisibility.Normal),
            (Frozen: (DateTimeOffset?)now.AddMinutes(-1), Hidden: (DateTimeOffset?)now.AddMinutes(1),
                Expected: CompetitionLeaderboardVisibility.Frozen),
            (Frozen: (DateTimeOffset?)now.AddMinutes(1), Hidden: (DateTimeOffset?)now.AddMinutes(-1),
                Expected: CompetitionLeaderboardVisibility.Blackout),
            (Frozen: (DateTimeOffset?)now, Hidden: (DateTimeOffset?)null,
                Expected: CompetitionLeaderboardVisibility.Frozen),
            (Frozen: (DateTimeOffset?)null, Hidden: (DateTimeOffset?)now,
                Expected: CompetitionLeaderboardVisibility.Blackout),
            (Frozen: (DateTimeOffset?)now.AddMinutes(-2), Hidden: (DateTimeOffset?)now.AddMinutes(-1),
                Expected: CompetitionLeaderboardVisibility.Blackout),
            (Frozen: (DateTimeOffset?)now.AddMinutes(-1), Hidden: (DateTimeOffset?)now.AddMinutes(-2),
                Expected: CompetitionLeaderboardVisibility.Frozen),
            (Frozen: (DateTimeOffset?)now, Hidden: (DateTimeOffset?)now,
                Expected: CompetitionLeaderboardVisibility.Blackout)
        };

        foreach (var item in cases)
        {
            await Assert.That(CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
                    item.Frozen,
                    item.Hidden,
                    now))
                .IsEqualTo(item.Expected);
        }
    }

    [Test]
    public async Task Blood_is_not_announced_while_blackout_is_effective()
    {
        var now = DateTimeOffset.UtcNow;
        await Assert.That(CompetitionLeaderboardVisibilityPolicy.CanAnnounceBlood(
            null, null, now)).IsTrue();
        await Assert.That(CompetitionLeaderboardVisibilityPolicy.CanAnnounceBlood(
            now.AddMinutes(-1), null, now)).IsTrue();
        await Assert.That(CompetitionLeaderboardVisibilityPolicy.CanAnnounceBlood(
            null, now.AddMinutes(-1), now)).IsFalse();
        await Assert.That(CompetitionLeaderboardVisibilityPolicy.CanAnnounceBlood(
            now.AddMinutes(-2), now.AddMinutes(-1), now)).IsFalse();
        await Assert.That(CompetitionLeaderboardVisibilityPolicy.CanAnnounceBlood(
            now.AddMinutes(-1), now.AddMinutes(-2), now)).IsTrue();
    }

    [Test]
    public async Task Visibility_timestamps_must_be_inside_competition_window()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new RecordingStore(View(now));
        var useCase = new UpdateCompetitionVisibility(store);

        var beforeCompetition = await useCase.ExecuteAsync(Command(
            now,
            frozenStartAt: now.AddMinutes(5)));
        var atCompetitionEnd = await useCase.ExecuteAsync(Command(
            now,
            hiddenStartAt: now.AddHours(2)));

        await Assert.That(beforeCompetition.State)
            .IsEqualTo(CompetitionVisibilityMutationState.InvalidSchedule);
        await Assert.That(atCompetitionEnd.State)
            .IsEqualTo(CompetitionVisibilityMutationState.InvalidSchedule);
        await Assert.That(store.Updates).IsEmpty();
    }

    [Test]
    public async Task Clearing_both_timestamps_restores_normal_visibility()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new RecordingStore(View(now) with
        {
            EffectiveVisibility = CompetitionLeaderboardVisibility.Blackout,
            HiddenStartAt = now.AddMinutes(10)
        });

        var result = await new UpdateCompetitionVisibility(store)
            .ExecuteAsync(Command(now));

        await Assert.That(result.State).IsEqualTo(CompetitionVisibilityMutationState.Updated);
        await Assert.That(store.Updates).HasSingleItem();
        await Assert.That(store.Updates[0].FrozenStartAt).IsNull();
        await Assert.That(store.Updates[0].HiddenStartAt).IsNull();
    }

    [Test]
    public async Task Finished_competition_accepts_only_normal_visibility()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new RecordingStore(View(now) with
        {
            CompetitionStatus = CompetitionStatus.Finished
        });

        var restricted = await new UpdateCompetitionVisibility(store)
            .ExecuteAsync(Command(now, frozenStartAt: now.AddMinutes(10)));
        var normal = await new UpdateCompetitionVisibility(store)
            .ExecuteAsync(Command(now));

        await Assert.That(restricted.State)
            .IsEqualTo(CompetitionVisibilityMutationState.CompetitionFinished);
        await Assert.That(normal.State).IsEqualTo(CompetitionVisibilityMutationState.Updated);
        await Assert.That(store.Updates).HasSingleItem();
    }

    private static CompetitionVisibilityConfigurationView View(DateTimeOffset now) =>
        new(
            Guid.CreateVersion7(),
            CompetitionStatus.Running,
            now.AddMinutes(10),
            now.AddHours(2),
            CompetitionLeaderboardVisibility.Normal,
            null,
            null);

    private static UpdateCompetitionVisibilityCommand Command(
        DateTimeOffset now,
        DateTimeOffset? frozenStartAt = null,
        DateTimeOffset? hiddenStartAt = null) =>
        new(
            Guid.CreateVersion7(),
            frozenStartAt,
            hiddenStartAt,
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
    }
}
