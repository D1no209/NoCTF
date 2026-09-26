using System.Text.Json;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Webhooks;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class CompetitionWebhookBloodContractTests
{
    [Test]
    [Arguments(CompetitionEventKind.FirstBloodAwarded, "First")]
    [Arguments(CompetitionEventKind.SecondBloodAwarded, "Second")]
    [Arguments(CompetitionEventKind.ThirdBloodAwarded, "Third")]
    public async Task Live_blood_requires_the_matching_public_team_and_challenge(
        CompetitionEventKind kind,
        string award)
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var source = Source(kind, teamId, challengeId);
        var complete = Body(teamId, challengeId, award, "Live", "Team", "Challenge");
        BloodWebhookContract.Validate(complete, source, LeaderboardDataScope.Live);

        var missingTeam = Body(teamId, challengeId, award, "Live", null, "Challenge");
        await Assert.That(() => BloodWebhookContract.Validate(
                missingTeam, source, LeaderboardDataScope.Live))
            .Throws<CompetitionWebhookProjectionNotReadyException>();
        var missingChallenge = Body(teamId, challengeId, award, "Live", "Team", null);
        await Assert.That(() => BloodWebhookContract.Validate(
                missingChallenge, source, LeaderboardDataScope.Live))
            .Throws<CompetitionWebhookProjectionNotReadyException>();
    }

    [Test]
    public async Task Frozen_blood_requires_a_frozen_snapshot()
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var source = Source(CompetitionEventKind.FirstBloodAwarded, teamId, challengeId);
        var frozen = Body(teamId, challengeId, "First", "Frozen", "Team", "Challenge",
            DateTimeOffset.UtcNow.AddMinutes(-1));
        BloodWebhookContract.Validate(frozen, source, LeaderboardDataScope.Frozen);

        var live = Body(teamId, challengeId, "First", "Live", "Team", "Challenge");
        await Assert.That(() => BloodWebhookContract.Validate(
                live, source, LeaderboardDataScope.Frozen))
            .Throws<CompetitionWebhookProjectionNotReadyException>();
    }

    [Test]
    public async Task Hidden_blood_is_never_a_deliverable_contract()
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var source = Source(CompetitionEventKind.FirstBloodAwarded, teamId, challengeId);
        await Assert.That(() => BloodWebhookContract.Validate(
                Body(teamId, challengeId, "First", "Hidden", null, null),
                source, LeaderboardDataScope.Hidden))
            .Throws<CompetitionWebhookProjectionNotReadyException>();

        await Assert.That(() => BloodWebhookContract.Validate(
                Body(teamId, challengeId, "First", "Hidden", "Team", "Challenge"),
                source, LeaderboardDataScope.Hidden))
            .Throws<CompetitionWebhookProjectionNotReadyException>();
    }

    private static CompetitionEvent Source(
        CompetitionEventKind kind, Guid teamId, Guid challengeId)
    {
        CompetitionEvent source = kind switch
        {
            CompetitionEventKind.FirstBloodAwarded => new FirstBloodAwardedEvent(),
            CompetitionEventKind.SecondBloodAwarded => new SecondBloodAwardedEvent(),
            CompetitionEventKind.ThirdBloodAwarded => new ThirdBloodAwardedEvent(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        source.TeamId = teamId;
        source.CompetitionChallengeId = challengeId;
        return source;
    }

    private static byte[] Body(
        Guid teamId, Guid challengeId, string award, string scope,
        string? teamName, string? challengeTitle, DateTimeOffset? dataAsOf = null) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            data = new
            {
                eventSequence = 1,
                requiredProjectionVersion = 1,
                publicProjectionVersion = 1,
                competitionRevision = Guid.NewGuid(),
                @event = new { teamId, competitionChallengeId = challengeId, award },
                resources = new
                {
                    challenge = challengeTitle is null
                        ? (object)new { id = challengeId }
                        : new { id = challengeId, title = challengeTitle,
                            projectionVersion = "1" },
                    leaderboard = new
                    {
                        dataScope = scope,
                        projectionVersion = "1",
                        dataAsOf,
                        teams = teamName is null
                            ? Array.Empty<object>()
                            : new object[] { new { teamId, teamName } }
                    }
                }
            }
        });
}
