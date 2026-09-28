using NSubstitute;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public sealed class ParticipantChallengeHintTests
{
    [Test]
    public async Task Read_filters_unpublished_future_and_deleted_hints_and_redacts_locked_content()
    {
        var now = DateTimeOffset.UtcNow;
        var free = Hint(0, now);
        var locked = Hint(20, now);
        var unlocked = Hint(30, now);
        var store = Substitute.For<IParticipantChallengeHintStore>();
        store.ReadAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CompetitionStatus>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new ParticipantChallengeHintAccess(
                [free, locked, unlocked, Hint(0, null), Hint(0, now.AddMinutes(1)), Hint(0, now) with { DeletedAt = now }],
                new HashSet<Guid> { unlocked.Id }, true));

        var result = await new ReadParticipantChallengeHints(store).ExecuteAsync(
            Guid.NewGuid(), Guid.NewGuid(), CompetitionStatus.Running, Guid.NewGuid(), now, CancellationToken.None);

        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result.Single(item => item.Id == free.Id).Content).IsEqualTo(free.Content);
        await Assert.That(result.Single(item => item.Id == free.Id).CanUnlock).IsFalse();
        await Assert.That(result.Single(item => item.Id == locked.Id).Content).IsNull();
        await Assert.That(result.Single(item => item.Id == locked.Id).CanUnlock).IsTrue();
        await Assert.That(result.Single(item => item.Id == unlocked.Id).Content).IsEqualTo(unlocked.Content);
        await Assert.That(result.Single(item => item.Id == unlocked.Id).IsUnlocked).IsTrue();
    }

    [Test]
    public async Task Read_does_not_offer_paid_unlock_without_eligible_team_or_scope()
    {
        var now = DateTimeOffset.UtcNow;
        var store = Substitute.For<IParticipantChallengeHintStore>();
        store.ReadAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CompetitionStatus>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new ParticipantChallengeHintAccess([Hint(20, now)], new HashSet<Guid>(), false));
        var useCase = new ReadParticipantChallengeHints(store);
        var result = await useCase.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), CompetitionStatus.Running, null, now, CancellationToken.None);
        await Assert.That(result.Single().CanUnlock).IsFalse();
        await Assert.That(result.Single().Content).IsNull();
        store.ReadAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CompetitionStatus>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((ParticipantChallengeHintAccess?)null);
        await Assert.That(await useCase.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), CompetitionStatus.Running, null, now, CancellationToken.None)).IsEmpty();
    }

    private static ChallengeHintView Hint(long cost, DateTimeOffset? publishedAt) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Protected hint body", cost, publishedAt, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
