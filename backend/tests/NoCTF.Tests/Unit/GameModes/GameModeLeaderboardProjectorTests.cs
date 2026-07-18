using NoCTF.Application.Scoring.Evaluation;
using NoCTF.Application.Scoring.Events;
using NoCTF.Application.Submissions.Events;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class GameModeLeaderboardProjectorTests
{
    [Test]
    public async Task Ctf_tie_break_uses_last_correct_solve_then_team_name()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        var early = DateTimeOffset.UtcNow.AddMinutes(-2);
        var late = early.AddMinutes(1);
        var context = new ScoringContext(
            competitionId,
            GameMode.Ctf,
            """{"schemaVersion":1}""",
            new Dictionary<Guid, ScoringTeam>
            {
                [alpha] = new(alpha, "Alpha", false, false),
                [bravo] = new(bravo, "Bravo", false, false)
            },
            new Dictionary<Guid, ScoringChallenge>
            {
                [challengeId] = new(challengeId, "Web", false, """{"schemaVersion":1}""")
            });
        var events = new IScoringStreamEvent[]
        {
            new ScoreAwarded(competitionId, alpha, challengeId, 100, ScoringReason.Solve, early, Guid.NewGuid()),
            new CtfSolveRecorded(competitionId, alpha, challengeId, early, Guid.NewGuid()),
            new ScoreAwarded(competitionId, bravo, challengeId, 100, ScoringReason.Solve, late, Guid.NewGuid()),
            new CtfSolveRecorded(competitionId, bravo, challengeId, late, Guid.NewGuid())
        };

        var snapshot = new CtfLeaderboardProjector().Project(context, 1, events);

        await Assert.That(snapshot.Entries[0].TeamId).IsEqualTo(alpha);
        await Assert.That(snapshot.Entries[0].Challenges.Single().Direction).IsEqualTo("Web");
    }

    [Test]
    public async Task Scoring_facts_do_not_expose_plain_flag_fields()
    {
        var eventTypes = typeof(IScoringStreamEvent).Assembly.GetTypes()
            .Where(type => typeof(IScoringStreamEvent).IsAssignableFrom(type) && type.IsClass);

        var exposesFlag = eventTypes.Any(type => type.GetProperties()
            .Any(property => property.Name.Contains("Flag", StringComparison.OrdinalIgnoreCase)));

        await Assert.That(exposesFlag).IsFalse();
    }
}
