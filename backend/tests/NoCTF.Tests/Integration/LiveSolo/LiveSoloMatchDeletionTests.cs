using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Brackets;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Integration.Persistence;
using Fixture = NoCTF.Tests.Integration.Persistence.LiveSoloMatchPersistenceTests.Fixture;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloMatchDeletionTests
{
    [Test, Timeout(300_000)]
    public Task Running_match_deletion_releases_teams_stops_resources_and_retains_facts(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await Fixture.CreateAsync(ct);
            await f.PrepareAsync(ct); await f.StartAsync(ct);
            var store = f.Store(f.Db);
            var question = f.Round.Questions.Single(x => x.Position == 0);
            var wrong = await store.AdmitAsync(new(f.Competition.Id, f.Match.Id, f.Round.Id, question.Id, f.Left.Id, "flag{wrong}", f.Now), ct);
            await store.ResolveAsync(f.Round.Id, f.Now, ct);
            var before = await f.Db.GameplayFacts.AsNoTracking().SingleAsync(x => x.Id == wrong.GameplayFactId, ct);
            var pending = await store.AdmitAsync(new(f.Competition.Id, f.Match.Id, f.Round.Id, question.Id, f.Right.Id, "flag{first}", f.Now), ct);
            var runtime = new PlayerRuntimeInstance { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, CompetitionChallengeId = question.CompetitionChallengeId,
                TeamId = f.LeftTeam.Id, ExecutionScopeId = question.Id, RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker,
                State = RuntimeState.Running, CreatedAt = f.Now, RunningAt = f.Now };
            f.Db.RuntimeInstances.Add(runtime);
            var session = await f.Db.LiveSoloMediaSessions.SingleAsync(ct);
            var grant = new LiveSoloMediaGrant { Id = Guid.NewGuid(), MediaSessionId = session.Id, UserId = f.Left.Id, Identity = "left-screen", IssuedAt = f.Now };
            f.Db.LiveSoloMediaGrants.Add(grant); await f.Db.SaveChangesAsync(ct);
            var command = new DeleteLiveSoloMatch(f.Competition.Id, f.Match.Id, f.Owner.Id, f.Now);
            await Assert.That((await store.DeleteAsync(command with { ActorId = f.Left.Id }, ct)).Failure).IsEqualTo(LiveSoloFailure.Forbidden);
            await Assert.That(await f.Db.LiveSoloMatches.CountAsync(ct)).IsEqualTo(1);
            var deleted = await store.DeleteAsync(command, ct);
            await Assert.That(deleted.Failure).IsNull();
            await Assert.That(deleted.MatchIds).IsEquivalentTo([f.Match.Id]);
            await using var db = new NoCtfDbContext(f.Options);
            var restarted = f.Store(db);
            await Assert.That((await restarted.ListAsync(f.Competition.Id, f.Owner.Id, true, f.Now, ct))!.Count).IsEqualTo(0);
            await Assert.That(await restarted.FindAsync(f.Competition.Id, f.Match.Id, f.Owner.Id, true, f.Now, ct)).IsNull();
            await Assert.That(await db.LiveSoloActiveTeamSlots.CountAsync(ct)).IsEqualTo(0);
            await Assert.That((await db.LiveSoloRounds.SingleAsync(ct)).State).IsEqualTo(LiveSoloRoundState.Canceled);
            var retained = await db.GameplayFacts.SingleAsync(x => x.Id == before.Id, ct);
            await Assert.That(retained.Result).IsEqualTo(before.Result);
            await Assert.That(retained.FailureCode).IsEqualTo(before.FailureCode);
            await Assert.That((await db.GameplayFacts.SingleAsync(x => x.Id == pending.GameplayFactId, ct)).FailureCode).IsEqualTo(GameplayFactFailureCode.RoundOutOfRange);
            await Assert.That((await db.RuntimeInstances.SingleAsync(ct)).State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That((await db.LiveSoloMediaSessions.SingleAsync(ct)).State).IsEqualTo(LiveSoloMediaState.Stopping);
            await Assert.That((await db.LiveSoloMediaGrants.SingleAsync(ct)).RevokedAt).IsNotNull();
            await Assert.That((await db.LiveSoloMatches.IgnoreQueryFilters().SingleAsync(ct)).DeletedByUserId).IsEqualTo(f.Owner.Id);
            await Assert.That((await restarted.DeleteAsync(command, ct)).Failure).IsNull();
            await Assert.That((await restarted.CreateAsync(new(f.Competition.Id, f.Owner.Id, f.LeftTeam.Id, f.RightTeam.Id, null, f.Now), ct)).Failure).IsNull();
            var history = (await restarted.CandidatesAsync(f.Competition.Id, (await db.LiveSoloMatches.SingleAsync(ct)).Id, f.Owner.Id, f.Now, ct))!;
            await Assert.That(history.Any(x => x.Uses.Any(use => use.MatchId == f.Match.Id))).IsTrue();
        });

    [Test, Timeout(300_000)]
    public Task Deleting_a_bracket_source_removes_dependents_and_allows_regeneration(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await Fixture.CreateAsync(ct, false);
            ((LiveSoloCompetitionModeConfiguration)f.Competition.ModeConfiguration!).BracketFormat = LiveSoloBracketFormat.DoubleElimination;
            await f.Db.SaveChangesAsync(ct);
            var store = f.Store(f.Db);
            var bracket = await store.GenerateAsync(new(f.Competition.Id, f.Owner.Id, f.Competition.ConcurrencyStamp, [f.LeftTeam.Id, f.RightTeam.Id], f.Now), ct);
            await Assert.That(bracket.Failure).IsNull();
            var source = bracket.Bracket!.Matches.Single(x => x.Lane == LiveSoloBracketLane.Winners);
            var deleted = await store.DeleteAsync(new(f.Competition.Id, source.Match.Id, f.Owner.Id, f.Now), ct);
            await Assert.That(deleted.Failure).IsNull();
            await Assert.That(deleted.MatchIds.Count).IsEqualTo(bracket.Bracket.Matches.Count);
            await Assert.That((await store.ReadAsync(f.Competition.Id, f.Owner.Id, ct))!.Matches.Count).IsEqualTo(0);
            var generated = await store.GenerateAsync(new(f.Competition.Id, f.Owner.Id, f.Competition.ConcurrencyStamp, [f.LeftTeam.Id, f.RightTeam.Id], f.Now), ct);
            await Assert.That(generated.Failure).IsNull();
            await Assert.That(generated.Bracket!.Matches.Count).IsEqualTo(bracket.Bracket.Matches.Count);
            await Assert.That(await f.Db.LiveSoloMatches.IgnoreQueryFilters().CountAsync(ct)).IsEqualTo(bracket.Bracket.Matches.Count * 2);
        });
}
