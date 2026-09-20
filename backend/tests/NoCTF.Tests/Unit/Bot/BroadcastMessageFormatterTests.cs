using NoCTF.Bot.Broadcasting;
using NoCTF.Bot.NoCtf;
using NoCTF.Bot.Persistence;

namespace NoCTF.Tests.Unit.Bot;

[Category("Bot")]
public sealed class BroadcastMessageFormatterTests
{
    [Test]
    public async Task Rank_HiddenScope_DoesNotExposeScores()
    {
        var snapshot = Scoreboard(LeaderboardVisibility.Blackout, LeaderboardDataScope.Hidden);

        var messages = BroadcastMessageFormatter.Rank(snapshot, 10);

        await Assert.That(messages).HasSingleItem();
        await Assert.That(messages[0]).Contains("当前不可见");
        await Assert.That(messages[0]).DoesNotContain("1000");
    }

    [Test]
    public async Task Create_HiddenScope_DoesNotEmitBloodDetails()
    {
        var competitionId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.Parse("2026-09-20T00:00:00Z");
        var competition = new Competition(
            competitionId,
            "Test CTF",
            null,
            "Ctf",
            occurredAt.AddHours(-1),
            occurredAt.AddHours(1),
            CompetitionStatus.Running,
            LeaderboardVisibility.Blackout,
            CompetitionAccessMode.Public);
        var previous = new CompetitionSnapshot(
            competitionId,
            CompetitionStatus.Running,
            competition.StartTime,
            competition.EndTime,
            "hash",
            occurredAt);

        var messages = BroadcastMessageFormatter.Create(
            competition,
            previous,
            new Dictionary<Guid, ChallengeSnapshot>(),
            [],
            new Dictionary<Guid, TeamSnapshot>(),
            [],
            Scoreboard(LeaderboardVisibility.Blackout, LeaderboardDataScope.Hidden),
            [new(Guid.NewGuid(), "FirstBloodAwarded", occurredAt)]);

        await Assert.That(messages.Any(message => message.Category == BroadcastCategory.Blood)).IsFalse();
        await Assert.That(messages.Any(message => message.Text.Contains("1000", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Create_PublicBloodSignal_UsesSnapshotTeamAndChallenge()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.Parse("2026-09-20T00:00:00Z");
        var achievement = new ScoreboardAchievement(
            challengeId,
            ScoreboardEntryKind.Solve,
            Guid.NewGuid(),
            "member",
            occurredAt);
        var team = new ScoreboardTeam(
            teamId,
            "Blue Team",
            "default",
            1,
            ScoreboardRankingState.Eligible,
            500,
            [achievement]);
        var challenge = new ChallengeSnapshot(
            competitionId,
            challengeId,
            "Web 100",
            true,
            "hash");
        var competition = new Competition(
            competitionId,
            "Test CTF",
            null,
            "Ctf",
            occurredAt.AddHours(-1),
            occurredAt.AddHours(1),
            CompetitionStatus.Running,
            LeaderboardVisibility.Normal,
            CompetitionAccessMode.Public);
        var previousCompetition = new CompetitionSnapshot(
            competitionId,
            CompetitionStatus.Running,
            competition.StartTime,
            competition.EndTime,
            "hash",
            occurredAt);
        var scoreboard = Scoreboard(
            LeaderboardVisibility.Normal,
            LeaderboardDataScope.Live) with { Teams = [team] };
        var teamJson = System.Text.Json.JsonSerializer.Serialize(new[] { achievement });
        var teamSnapshot = new TeamSnapshot(
            competitionId,
            teamId,
            team.TeamName,
            team.Rank,
            team.TotalScore,
            "achievement-hash",
            teamJson);

        var messages = BroadcastMessageFormatter.Create(
            competition,
            previousCompetition,
            new Dictionary<Guid, ChallengeSnapshot> { [challengeId] = challenge },
            [challenge],
            new Dictionary<Guid, TeamSnapshot>(),
            [teamSnapshot],
            scoreboard,
            [new(Guid.NewGuid(), "FirstBloodAwarded", occurredAt)]);

        await Assert.That(messages.Any(message =>
            message.Category == BroadcastCategory.Blood
            && message.Text.Contains("Blue Team", StringComparison.Ordinal)
            && message.Text.Contains("Web 100", StringComparison.Ordinal)
            && message.Text.Contains("一血", StringComparison.Ordinal))).IsTrue();
    }

    private static ScoreboardSnapshot Scoreboard(
        LeaderboardVisibility visibility,
        LeaderboardDataScope dataScope) =>
        new(
            Guid.NewGuid(),
            "1",
            "1",
            DateTimeOffset.Parse("2026-09-20T00:00:00Z"),
            [new(
                Guid.NewGuid(),
                "Red Team",
                "default",
                1,
                ScoreboardRankingState.Eligible,
                1000,
                [])],
            visibility,
            dataScope,
            null);
}
