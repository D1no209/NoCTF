using NSubstitute;
using NoCTF.Application.Teams.Profiles;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;

namespace NoCTF.Tests.Unit.Application;

public sealed class TeamProfileTests
{
    [Test]
    public async Task CreateTeamProfile_TrimsNameBeforePersistence()
    {
        var store = Substitute.For<ITeamProfileStore>();
        CreateTeamProfileCommand? persisted = null;
        store.CreateAsync(
                Arg.Do<CreateTeamProfileCommand>(command => persisted = command),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var command = call.ArgAt<CreateTeamProfileCommand>(0)!;
                return new TeamProfileMutationResult(Profile(
                    command.UserId,
                    command.Name));
            });

        var result = await new CreateTeamProfile(store).ExecuteAsync(new(
            Guid.NewGuid(),
            "  Alpha  ",
            null,
            DateTimeOffset.UtcNow));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(persisted!.Name).IsEqualTo("Alpha");
    }

    [Test]
    public async Task RegisterTeamProfile_SnapshotsCurrentRoster()
    {
        var captainId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var teamProfileId = Guid.NewGuid();
        var competitionId = Guid.NewGuid();
        var profiles = Substitute.For<ITeamProfileStore>();
        profiles.FindAsync(teamProfileId, Arg.Any<CancellationToken>())
            .Returns(new TeamProfileView(
                teamProfileId,
                "Alpha",
                null,
                captainId,
                [captainId, memberId],
                new string('t', 32),
                DateTimeOffset.UtcNow));
        var registrations = Substitute.For<ITeamRegistrationStore>();
        registrations.GetPolicyAsync(competitionId, Arg.Any<CancellationToken>())
            .Returns(new TeamRegistrationPolicy(
                CompetitionStatus.Published,
                true,
                false,
                5));
        CreateTeamCommand? persisted = null;
        registrations.TryCreateAsync(
                Arg.Do<CreateTeamCommand>(command => persisted = command),
                TeamRegistrationStatus.Approved,
                Arg.Any<CancellationToken>())
            .Returns(call => new TeamCreateStoreResult(new TeamView(
                Guid.NewGuid(),
                competitionId,
                call.ArgAt<CreateTeamCommand>(0).Name,
                null,
                captainId,
                [captainId, memberId],
                TeamRegistrationStatus.Approved,
                false,
                false,
                DateTimeOffset.UtcNow,
                teamProfileId)));

        var result = await new RegisterTeamProfile(
            profiles,
            new CreateTeam(registrations)).ExecuteAsync(
            competitionId,
            teamProfileId,
            captainId,
            DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(persisted!.TeamProfileId).IsEqualTo(teamProfileId);
        await Assert.That(persisted.CaptainId).IsEqualTo(captainId);
        await Assert.That(persisted.MemberIds).IsEquivalentTo([captainId, memberId]);
    }

    [Test]
    public async Task RegisterTeamProfile_RejectsRosterAboveCompetitionLimit()
    {
        var captainId = Guid.NewGuid();
        var teamProfileId = Guid.NewGuid();
        var competitionId = Guid.NewGuid();
        var profiles = Substitute.For<ITeamProfileStore>();
        profiles.FindAsync(teamProfileId, Arg.Any<CancellationToken>())
            .Returns(new TeamProfileView(
                teamProfileId,
                "Alpha",
                null,
                captainId,
                [captainId, Guid.NewGuid()],
                new string('t', 32),
                DateTimeOffset.UtcNow));
        var registrations = Substitute.For<ITeamRegistrationStore>();
        registrations.GetPolicyAsync(competitionId, Arg.Any<CancellationToken>())
            .Returns(new TeamRegistrationPolicy(
                CompetitionStatus.Published,
                true,
                false,
                1));

        var result = await new RegisterTeamProfile(
            profiles,
            new CreateTeam(registrations)).ExecuteAsync(
            competitionId,
            teamProfileId,
            captainId,
            DateTimeOffset.UtcNow);

        await Assert.That(result.ErrorCode).IsEqualTo("team_full");
        await registrations.DidNotReceiveWithAnyArgs().TryCreateAsync(
            default!,
            default,
            default);
    }

    private static TeamProfileView Profile(Guid captainId, string name) => new(
        Guid.NewGuid(),
        name,
        null,
        captainId,
        [captainId],
        new string('t', 32),
        DateTimeOffset.UtcNow);
}
