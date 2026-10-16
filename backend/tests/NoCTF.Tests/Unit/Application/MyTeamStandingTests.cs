using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.Registration;
using NoCTF.Application.Teams.Standings;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;

namespace NoCTF.Tests.Unit.Application;

public sealed class MyTeamStandingTests
{
    [Test]
    public async Task Returns_only_the_current_team_from_the_published_track_filtered_projection()
    {
        var f = new Fixture();
        var result = await f.Read();
        await Assert.That(result.State).IsEqualTo(MyTeamStandingState.Ready);
        await Assert.That(result.Standing!.TeamId).IsEqualTo(f.Team.Id);
        await Assert.That(result.Standing.TotalScore).IsEqualTo(42);
        await Assert.That(result.Standing.ChallengeBenefits.Single().CompetitionChallengeId).IsEqualTo(f.ChallengeId);
        await Assert.That(result.Standing.Version).IsEqualTo(7);
    }

    [Test]
    public async Task Frozen_reads_use_the_frozen_generation_and_hidden_reads_never_read_a_snapshot()
    {
        var f = new Fixture();
        f.SetScope(LeaderboardDataScope.Frozen);
        var frozen = await f.Read();
        await Assert.That(frozen.Standing!.TotalScore).IsEqualTo(12);
        await Assert.That(frozen.Standing.DataAsOf).IsEqualTo(f.Now.AddMinutes(-10));
        await f.Cache.DidNotReceive().GetScoreboardAsync(f.Id, Arg.Any<CancellationToken>());
        f.Cache.ClearReceivedCalls();
        f.SetScope(LeaderboardDataScope.Hidden);
        var hidden = await f.Read();
        await Assert.That(hidden.Standing!.TotalScore).IsNull();
        await Assert.That(hidden.Standing.Rank).IsNull();
        await Assert.That(hidden.Standing.ChallengeBenefits).IsEmpty();
        await f.Cache.DidNotReceive().GetFrozenScoreboardAsync(f.Id, Arg.Any<CancellationToken>());
        await f.Cache.DidNotReceive().GetScoreboardAsync(f.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Missing_projection_requests_rebuild_and_reports_processing_or_failure()
    {
        var f = new Fixture();
        f.Cache.GetScoreboardAsync(f.Id, Arg.Any<CancellationToken>()).Returns((ScoreboardProjection?)null);
        f.Cache.GetStatusAsync(f.Id, Arg.Any<CancellationToken>()).Returns(new LeaderboardCacheStatus(null));
        await Assert.That((await f.Read()).State).IsEqualTo(MyTeamStandingState.Processing);
        await f.Cache.Received(1).InvalidateAsync(f.Id, Arg.Any<CancellationToken>());
        f.Cache.GetStatusAsync(f.Id, Arg.Any<CancellationToken>()).Returns(new LeaderboardCacheStatus(f.Now));
        await Assert.That((await f.Read()).State).IsEqualTo(MyTeamStandingState.ProjectionFailed);
    }

    [Test]
    public async Task Pending_banned_missing_and_track_hidden_teams_do_not_leak_scores()
    {
        var f = new Fixture();
        foreach (var team in new TeamView?[] { null,
            f.Team with { RegistrationStatus = TeamRegistrationStatus.Pending }, f.Team with { IsBanned = true } })
        {
            f.Teams.FindForUserAsync(f.Id, f.Actor, true, Arg.Any<CancellationToken>()).Returns(team);
            await Assert.That((await f.Read()).State).IsEqualTo(MyTeamStandingState.NotFound);
        }
        f.Teams.FindForUserAsync(f.Id, f.Actor, true, Arg.Any<CancellationToken>()).Returns(f.Team);
        f.Tracks.GetAsync(f.Id, f.Actor, false, false, Arg.Any<CancellationToken>()).Returns(
            new CompetitionTracksView(f.Id, GameMode.Ctf, CompetitionStatus.Running, true, false, []));
        await Assert.That((await f.Read()).Standing!.TotalScore).IsNull();
    }

    [Test]
    public async Task A_visibility_or_membership_change_during_the_read_discards_the_old_result()
    {
        var f = new Fixture();
        f.Access.ResolveAsync(f.Actor, f.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(f.Permission(LeaderboardDataScope.Live), f.Permission(LeaderboardDataScope.Hidden));
        await Assert.That((await f.Read()).Standing!.TotalScore).IsNull();
        f.SetScope(LeaderboardDataScope.Live);
        f.Teams.FindForUserAsync(f.Id, f.Actor, true, Arg.Any<CancellationToken>())
            .Returns(f.Team, f.Team with { IsBanned = true });
        await Assert.That((await f.Read()).State).IsEqualTo(MyTeamStandingState.NotFound);
    }

    private sealed class Fixture
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid Actor { get; } = Guid.NewGuid();
        public Guid ChallengeId { get; } = Guid.NewGuid();
        public DateTimeOffset Now { get; } = DateTimeOffset.Parse("2026-10-16T00:00:00Z");
        public ITeamRegistrationStore Teams { get; } = Substitute.For<ITeamRegistrationStore>();
        public ICompetitionVisibilityAccess Access { get; } = Substitute.For<ICompetitionVisibilityAccess>();
        public ICompetitionTrackStore Tracks { get; } = Substitute.For<ICompetitionTrackStore>();
        public ILeaderboardCache Cache { get; } = Substitute.For<ILeaderboardCache>();
        public TeamView Team { get; }
        private GetMyTeamStanding Query { get; }
        public CompetitionVisibilityAccessDecision Permission(LeaderboardDataScope scope) =>
            new(GameMode.Ctf, CompetitionStatus.Running, scope == LeaderboardDataScope.Live
                ? CompetitionLeaderboardVisibility.Normal : scope == LeaderboardDataScope.Frozen
                    ? CompetitionLeaderboardVisibility.Frozen : CompetitionLeaderboardVisibility.Blackout, scope);
        public void SetScope(LeaderboardDataScope scope) => Access.ResolveAsync(Actor, Id,
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Permission(scope));
        public Task<MyTeamStandingResult> Read() => Query.ExecuteAsync(Id, Actor, default);
        public Fixture()
        {
            Team = new(Guid.NewGuid(), Id, "Mine", null, Actor, [Actor], TeamRegistrationStatus.Approved, false, false, Now);
            Teams.FindForUserAsync(Id, Actor, true, Arg.Any<CancellationToken>()).Returns(Team);
            SetScope(LeaderboardDataScope.Live);
            Tracks.GetAsync(Id, Actor, false, false, Arg.Any<CancellationToken>()).Returns(
                new CompetitionTracksView(Id, GameMode.Ctf, CompetitionStatus.Running, true, false,
                    [new("default", "Public", true, true, false, true, true, true, true, true)]));
            ScoreboardProjection Projection(long score) {
                var projection = new ScoreboardProjection(new(Id, 1, []), new(Id, GameMode.Ctf, 1, 1, [], []),
                    new(Id, 7, 1, Now, null, [], [
                        new(Team.Id, "Mine", "default", 1, ScoreboardRankingState.Eligible, score, 0, [], [])
                            { ChallengeBenefits = [new(ChallengeId, score, 0, null, null)] },
                        new(Guid.NewGuid(), "Other", "default", 2, ScoreboardRankingState.Eligible, 999, 0, [], [])
                    ]) { DataAsOf = Now.AddMinutes(-10) });
                return projection with { ParticipantView = ScoreboardAudienceView.From(projection) };
            }
            Cache.GetScoreboardAsync(Id, Arg.Any<CancellationToken>()).Returns(Projection(42));
            Cache.GetFrozenScoreboardAsync(Id, Arg.Any<CancellationToken>()).Returns(Projection(12));
            Query = new(new(Teams), Access, Cache, new(Tracks), Substitute.For<ICompetitionModerationAuthorizer>(),
                Substitute.For<IBackendMessagePublisher>(), new FakeTimeProvider(Now));
        }
    }
}
