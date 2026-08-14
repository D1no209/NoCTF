using NSubstitute;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Domain.Challenges;

namespace NoCTF.Tests.Unit.Application;

public sealed class ChallengeFlagManagementTests
{
    [Test]
    public async Task Owner_can_save_a_supported_regular_expression()
    {
        var store = Substitute.For<IChallengeFlagStore>();
        var challengeId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var command = Command(challengeId, @"flag\{[0-9a-f-]{36}\}");
        var saved = new ChallengeFlagView(
            Guid.NewGuid(), challengeId, null, null, command.Flag,
            ChallengeFlagMatchKind.RegularExpression, null, null, null, null, null,
            command.Now);
        store.SupportsRegularExpressionAsync(
                command.Scope, actorId, false, Arg.Any<CancellationToken>())
            .Returns(true);
        store.SupportsManualStaticFlagsAsync(
                command.Scope, actorId, false, Arg.Any<CancellationToken>())
            .Returns(true);
        store.SaveAsync(command, actorId, false, Arg.Any<CancellationToken>())
            .Returns(new ChallengeFlagSaveResult(saved));

        var result = await new ManageChallengeFlags(store)
            .SaveAsync(command, actorId, false);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.MatchKind)
            .IsEqualTo(ChallengeFlagMatchKind.RegularExpression);
        await store.Received(1).SaveAsync(command, actorId, false, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Invalid_or_dynamic_regular_expression_is_rejected_without_a_write()
    {
        var store = Substitute.For<IChallengeFlagStore>();
        var challengeId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var invalid = Command(challengeId, "(?=unsupported-lookahead)flag");
        var dynamic = Command(challengeId, @"flag\{.*\}");
        store.SupportsManualStaticFlagsAsync(
                Arg.Any<ChallengeFlagScope>(), actorId, false, Arg.Any<CancellationToken>())
            .Returns(true);
        store.SupportsRegularExpressionAsync(
                dynamic.Scope, actorId, false, Arg.Any<CancellationToken>())
            .Returns(false);
        var useCase = new ManageChallengeFlags(store);

        var invalidResult = await useCase.SaveAsync(invalid, actorId, false);
        var dynamicResult = await useCase.SaveAsync(dynamic, actorId, false);

        await Assert.That(invalidResult.FailureCode)
            .IsEqualTo(ChallengeFlagFailureCode.InvalidRegularExpression);
        await Assert.That(dynamicResult.FailureCode)
            .IsEqualTo(ChallengeFlagFailureCode.RegularExpressionNotSupported);
        await store.DidNotReceiveWithAnyArgs().SaveAsync(default!, default, default, default);
    }

    [Test]
    public async Task Updating_a_system_managed_flag_reports_the_read_only_failure_first()
    {
        var store = Substitute.For<IChallengeFlagStore>();
        var competitionId = Guid.NewGuid();
        var competitionChallengeId = Guid.NewGuid();
        var flagId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var scope = ChallengeFlagScope.Competition(competitionId, competitionChallengeId);
        var existing = new ChallengeFlagView(
            flagId,
            null,
            competitionChallengeId,
            Guid.NewGuid(),
            "flag{generated}",
            ChallengeFlagMatchKind.Exact,
            SpecificationKind.RuntimeDefinition,
            competitionChallengeId,
            null,
            null,
            null,
            now);
        store.FindAsync(scope, flagId, actorId, false, false, Arg.Any<CancellationToken>())
            .Returns(existing);
        store.SupportsManualStaticFlagsAsync(
                scope, actorId, false, Arg.Any<CancellationToken>())
            .Returns(false);
        var command = new SaveChallengeFlagCommand(
            scope,
            flagId,
            false,
            existing.TeamId,
            "flag{replacement}",
            existing.SpecificationKind,
            existing.SpecificationId,
            null,
            null,
            now,
            ChallengeFlagMatchKind.Exact);

        var result = await new ManageChallengeFlags(store)
            .SaveAsync(command, actorId, false);

        await Assert.That(result.FailureCode)
            .IsEqualTo(ChallengeFlagFailureCode.SystemManagedFlag);
        await store.DidNotReceiveWithAnyArgs()
            .SupportsManualStaticFlagsAsync(default, default, default, default);
        await store.DidNotReceiveWithAnyArgs().SaveAsync(default!, default, default, default);
    }

    private static SaveChallengeFlagCommand Command(Guid challengeId, string pattern) => new(
        ChallengeFlagScope.Template(challengeId),
        null,
        true,
        null,
        pattern,
        null,
        null,
        null,
        null,
        DateTimeOffset.UtcNow,
        ChallengeFlagMatchKind.RegularExpression);
}
