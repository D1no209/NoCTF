using NSubstitute;
using NoCTF.Application.Teams.Appeals;

namespace NoCTF.Tests.Unit.Application;

public sealed class TeamBanAppealTests
{
    [Test]
    public async Task Submission_requires_a_bounded_statement_and_trims_it()
    {
        var store = Substitute.For<ITeamBanAppealStore>();
        store.SubmitAsync(Arg.Any<SubmitTeamBanAppealCommand>(), Arg.Any<CancellationToken>())
            .Returns(new TeamBanAppealMutationResult());
        var useCase = new SubmitTeamBanAppeal(store);
        var invalid = await useCase.ExecuteAsync(new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "too short",
            DateTimeOffset.UtcNow));
        await Assert.That(invalid.Failure).IsEqualTo(TeamBanAppealFailure.InvalidStatement);
        await store.DidNotReceive().SubmitAsync(
            Arg.Any<SubmitTeamBanAppealCommand>(),
            Arg.Any<CancellationToken>());

        var command = new SubmitTeamBanAppealCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "  Please review this moderation decision.  ",
            DateTimeOffset.UtcNow);
        var valid = await useCase.ExecuteAsync(command);
        await Assert.That(valid.Succeeded).IsTrue();
        await store.Received(1).SubmitAsync(
            Arg.Is<SubmitTeamBanAppealCommand>(received =>
                received != null
                && received.Statement == "Please review this moderation decision."),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Appeal_resolution_and_direct_correction_require_a_private_reason()
    {
        var store = Substitute.For<ITeamBanAppealStore>();
        var resolve = new ResolveTeamBanAppeal(store);
        var correct = new CorrectTeamBan(store);
        var now = DateTimeOffset.UtcNow;

        var invalidResolution = await resolve.ExecuteAsync(new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            TeamBanAppealResolution.Accept,
            "short",
            now));
        var invalidCorrection = await correct.ExecuteAsync(new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "short",
            now));

        await Assert.That(invalidResolution.Failure)
            .IsEqualTo(TeamBanAppealFailure.InvalidReason);
        await Assert.That(invalidCorrection.Failure)
            .IsEqualTo(TeamBanAppealFailure.InvalidReason);
        await store.DidNotReceive().ResolveAsync(
            Arg.Any<ResolveTeamBanAppealCommand>(),
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().CorrectAsync(
            Arg.Any<CorrectTeamBanCommand>(),
            Arg.Any<CancellationToken>());
    }
}
