using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public sealed class SpectatorScreenTests
{
    [Test]
    public async Task Redacted_control_is_unknown_and_never_fabricated_as_uncontrolled()
    {
        var challenge = Guid.NewGuid(); var hidden = Guid.NewGuid(); var publicTeam = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var frame = new SpectatorFrame("Test", CompetitionStatus.Running, null, [],
            [new(challenge, hidden, hidden, SpectatorHealth.Healthy, now, 5, 10)],
            [new(Guid.NewGuid(), Guid.NewGuid(), now, SpectatorChangeKind.Control, challenge, hidden, null, hidden),
                new(Guid.NewGuid(), Guid.NewGuid(), now, SpectatorChangeKind.DetectionFailed, challenge, hidden, null, publicTeam)], now, false);
        var filtered = SpectatorAudience.Filter(frame, new HashSet<Guid> { publicTeam }, new HashSet<Guid> { challenge });
        await Assert.That(filtered.Points.Single().ControllerId).IsNull();
        await Assert.That(filtered.Points.Single().Health).IsEqualTo(SpectatorHealth.Unknown);
        await Assert.That(filtered.Changes.Count).IsEqualTo(2);
        await Assert.That(filtered.Changes.All(x => x.Kind == SpectatorChangeKind.DetectionFailed && x.TeamId is null)).IsTrue();
        await Assert.That(filtered.Changes.Last().ControllerId).IsEqualTo(publicTeam);
    }

    [Test]
    public async Task Public_reads_filter_internal_teams_and_never_request_operator_permissions()
    {
        var f = new Fixture();
        var result = await f.Query.ExecuteAsync(f.Id, GameMode.Koh, 10, null, default);
        await Assert.That(result.State).IsEqualTo(SpectatorReadState.Ready);
        await Assert.That(result.Screen!.Teams.Select(x => x.Id)).IsEquivalentTo([f.PublicTeam]);
        await Assert.That(result.Screen.Frame!.Services.Select(x => x.TeamId)).IsEquivalentTo([f.PublicTeam]);
        await Assert.That(result.Screen.Frame.Points.Single().ObservedTeamId).IsNull();
        await f.Access.Received().ResolveAsync(Guid.Empty, f.Id, f.Now, Arg.Any<CancellationToken>());
        await f.Tracks.Received().GetAsync(f.Id, null, false, false, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Hidden_and_revoked_reads_clear_every_display_channel()
    {
        var f = new Fixture();
        f.SetScope(LeaderboardDataScope.Hidden);
        var hidden = (await f.Query.ExecuteAsync(f.Id, GameMode.Koh, 10, null, default)).Screen!;
        await Assert.That(hidden.Teams).IsEmpty();
        await Assert.That(hidden.Challenges).IsEmpty();
        await Assert.That(hidden.Frame).IsNull();
        await Assert.That(hidden.Settlements).IsEmpty();
        await f.Cache.DidNotReceive().GetScoreboardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        f.Access.ResolveAsync(Arg.Any<Guid>(), f.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((CompetitionVisibilityAccessDecision?)null);
        await Assert.That((await f.Query.ExecuteAsync(f.Id, GameMode.Koh, 10, null, default)).State).IsEqualTo(SpectatorReadState.NotFound);
    }

    [Test]
    public async Task Visibility_is_rechecked_after_projection_read()
    {
        var f = new Fixture();
        f.Access.ResolveAsync(Arg.Any<Guid>(), f.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(f.Permission(LeaderboardDataScope.Live), f.Permission(LeaderboardDataScope.Hidden));
        var result = await f.Query.ExecuteAsync(f.Id, GameMode.Koh, 10, null, default);
        await Assert.That(result.Screen!.Scope).IsEqualTo(LeaderboardDataScope.Hidden);
        await Assert.That(result.Screen.Frame).IsNull();
    }

    [Test]
    public async Task Frozen_read_uses_one_saved_generation_and_missing_frames_are_processing()
    {
        var f = new Fixture(); f.SetScope(LeaderboardDataScope.Frozen);
        var result = await f.Query.ExecuteAsync(f.Id, GameMode.Koh, 10, null, default);
        await Assert.That(result.Screen!.DataAsOf).IsEqualTo(f.Now.AddMinutes(-1));
        await f.Cache.Received().GetFrozenSpectatorScoreboardAsync(f.Id, Arg.Any<CancellationToken>());
        await f.Cache.DidNotReceive().GetScoreboardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        f.Cache.GetFrozenSpectatorScoreboardAsync(f.Id, Arg.Any<CancellationToken>()).Returns(f.Projection with { Spectator = null });
        await Assert.That((await f.Query.ExecuteAsync(f.Id, GameMode.Koh, 10, null, default)).State).IsEqualTo(SpectatorReadState.Processing);
    }

    [Test]
    public async Task Making_an_internal_track_public_does_not_expand_an_existing_frozen_frame()
    {
        var f = new Fixture(); f.SetScope(LeaderboardDataScope.Frozen);
        f.Tracks.GetAsync(f.Id, null, false, false, Arg.Any<CancellationToken>()).Returns(new CompetitionTracksView(f.Id, GameMode.Koh,
            CompetitionStatus.Running, true, false, [new("default", "Public", true, true, false, true, true, true, true, true),
                new("internal", "Now public", false, true, false, true, true, true, true, true)]));
        var result = await f.Query.ExecuteAsync(f.Id, GameMode.Koh, 10, null, default);
        await Assert.That(result.Screen!.Teams.Select(x => x.Id)).IsEquivalentTo([f.PublicTeam]);
    }

    [Test]
    public async Task History_cursor_is_bound_to_version_and_window_and_wrong_mode_is_not_found()
    {
        var f = new Fixture();
        var first = (await f.Query.ExecuteAsync(f.Id, GameMode.Koh, 10, null, default)).Screen!;
        await Assert.That(first.Frame!.Changes.Count).IsEqualTo(256);
        var second = (await f.Query.ExecuteAsync(f.Id, GameMode.Koh, 10, first.NextCursor, default)).Screen!;
        await Assert.That(second.Frame!.Changes.Count).IsEqualTo(44);
        await Assert.That(first.Frame.Changes.Select(x => x.Id).Intersect(second.Frame.Changes.Select(x => x.Id))).IsEmpty();
        await Assert.That((await f.Query.ExecuteAsync(f.Id, GameMode.Koh, 5, first.NextCursor, default)).State).IsEqualTo(SpectatorReadState.CursorExpired);
        f.Cache.GetScoreboardAsync(f.Id, Arg.Any<CancellationToken>()).Returns(f.Projection with { Snapshot = f.Projection.Snapshot with { Version = 2 } });
        await Assert.That((await f.Query.ExecuteAsync(f.Id, GameMode.Koh, 10, first.NextCursor, default)).State).IsEqualTo(SpectatorReadState.CursorExpired);
        await Assert.That((await f.Query.ExecuteAsync(f.Id, GameMode.Koh, 10, "not-base64", default)).State).IsEqualTo(SpectatorReadState.CursorExpired);
        await Assert.That((await f.Query.ExecuteAsync(f.Id, GameMode.Awd, 10, null, default)).State).IsEqualTo(SpectatorReadState.NotFound);
    }

    private sealed class Fixture
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid PublicTeam { get; } = Guid.NewGuid();
        public DateTimeOffset Now { get; } = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
        public ILeaderboardCache Cache { get; } = Substitute.For<ILeaderboardCache>();
        public ICompetitionVisibilityAccess Access { get; } = Substitute.For<ICompetitionVisibilityAccess>();
        public ICompetitionTrackStore Tracks { get; } = Substitute.For<ICompetitionTrackStore>();
        public ScoreboardProjection Projection { get; }
        public GetSpectatorScreen Query { get; }
        public CompetitionVisibilityAccessDecision Permission(LeaderboardDataScope scope) =>
            new(GameMode.Koh, CompetitionStatus.Running, CompetitionLeaderboardVisibility.Normal, scope);
        public void SetScope(LeaderboardDataScope scope) => Access.ResolveAsync(Arg.Any<Guid>(), Id,
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Permission(scope));
        public Fixture()
        {
            var secret = Guid.NewGuid(); var challenge = Guid.NewGuid();
            var cutoff = Now.AddMinutes(-1);
            var frame = new SpectatorFrame("Public", CompetitionStatus.Running, null,
                [new(PublicTeam, challenge, SpectatorHealth.Healthy, cutoff), new(secret, challenge, SpectatorHealth.Down, cutoff)],
                [new(challenge, PublicTeam, secret, SpectatorHealth.Unknown, cutoff, 5, 10)],
                Enumerable.Range(0, 300).Select(i => new SpectatorChange(Guid.NewGuid(), Guid.NewGuid(), cutoff.AddSeconds(-i),
                    SpectatorChangeKind.Control, challenge, PublicTeam, null, PublicTeam)).ToArray(), cutoff.AddMinutes(-30), false);
            Projection = new(new(Id, 1, [new(challenge, "Point", "Web", "Web", 0, true)]),
                new(Id, GameMode.Koh, 1, 1, [], []),
                new(Id, 1, 1, cutoff, null, [], [
                    new(PublicTeam, "Public", "default", 1, ScoreboardRankingState.Eligible, 42, 0, [], []),
                    new(secret, "Secret", "internal", 2, ScoreboardRankingState.Eligible, 999, 0, [], [])])
                { DataAsOf = cutoff, Tracks = [new("default", "Public", false, true), new("internal", "Secret", true, false)] });
            Projection = Projection with { Spectator = frame, ParticipantView = ScoreboardAudienceView.From(Projection) };
            Cache.GetScoreboardAsync(Id, Arg.Any<CancellationToken>()).Returns(Projection);
            Cache.GetFrozenSpectatorScoreboardAsync(Id, Arg.Any<CancellationToken>()).Returns(Projection);
            Tracks.GetAsync(Id, null, false, false, Arg.Any<CancellationToken>()).Returns(new CompetitionTracksView(Id, GameMode.Koh,
                CompetitionStatus.Running, true, false, [new("default", "Public", true, true, false, true, true, true, true, true)]));
            SetScope(LeaderboardDataScope.Live);
            Query = new(Cache, Access, new(Tracks), Substitute.For<IBackendMessagePublisher>(), new FakeTimeProvider(Now));
        }
    }
}
