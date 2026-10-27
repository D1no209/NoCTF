using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.Runtime.Access;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Resources;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;
using Fixture = NoCTF.Tests.Integration.Persistence.LiveSoloMatchPersistenceTests.Fixture;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloDirectionPoolPersistenceTests
{
    [Test, Timeout(300_000)]
    public Task Batch_presets_bind_each_list_atomically_and_reuse_existing_bindings(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await Fixture.CreateAsync(ct, false);
            var store = f.Store(f.Db);
            var groups = new List<Guid>();
            for (var i = 0; i < 2; i++)
            {
                var group = await store.SaveGroupAsync(f.Competition.Id, f.Owner.Id, new(null, "Web list " + i, false, null,
                    [new(f.Entries[i].Id, 0)], null), f.Now, ct);
                groups.Add(group.Group!.Id);
            }
            var invalid = await store.SavePresetsAsync(f.Competition.Id, f.Owner.Id, "Web", [groups[0], Guid.NewGuid()], ct);
            await Assert.That(invalid.Failure).IsEqualTo(LiveSoloFailure.QuestionGroupNotFound);
            await Assert.That(await f.Db.Set<LiveSoloDirectionPreset>().CountAsync(ct)).IsEqualTo(0);
            await Assert.That((await store.SavePresetsAsync(f.Competition.Id, f.Left.Id, "Web", groups, ct)).Failure).IsEqualTo(LiveSoloFailure.Forbidden);
            var saved = await store.SavePresetsAsync(f.Competition.Id, f.Owner.Id, " Web ", groups, ct);
            await Assert.That(saved.Failure).IsNull();
            await Assert.That(saved.Presets.Select(x => x.QuestionGroupId).ToArray()).IsEquivalentTo(groups);
            await Assert.That(saved.Presets.Select(x => x.Name).ToArray()).IsEquivalentTo(["Web list 0", "Web list 1"]);
            await Assert.That(saved.Presets.All(x => x.Direction == "Web")).IsTrue();
            var repeated = await store.SavePresetsAsync(f.Competition.Id, f.Owner.Id, "Web", groups, ct);
            await Assert.That(repeated.Presets.Select(x => x.Id).ToArray()).IsEquivalentTo(saved.Presets.Select(x => x.Id));
            await Assert.That(await f.Db.Set<LiveSoloDirectionPreset>().CountAsync(ct)).IsEqualTo(2);
        });

    [Test, Arguments(0), Arguments(3), Timeout(300_000)]
    public async Task Bo7_draws_independent_slots_once_resets_rosters_and_stops_at_four_wins(int opposingWins, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await Fixture.CreateAsync(ct, false);
            var (matchId, presets) = await SetupAsync(f, ct);
            var store = f.Store(f.Db);
            var rounds = 4 + opposingWins;
            for (var i = 0; i < rounds; i++)
            {
                var drawn = await store.DrawAsync(f.Competition.Id, matchId, f.Owner.Id, f.Now, ct);
                await Assert.That(drawn.Failure).IsNull();
                await Assert.That(drawn.Match!.Rosters.All(x => !x.Locked && x.UserIds.Count == 0)).IsTrue();
                await using (var restarted = new NoCtfDbContext(f.Options))
                {
                    var repeated = await f.Store(restarted).DrawAsync(f.Competition.Id, matchId, f.Owner.Id, f.Now, ct);
                    await Assert.That(repeated.Failure).IsNull();
                }
                await Assert.That(await f.Db.Set<LiveSoloMatchDirection>().CountAsync(x => x.MatchId == matchId && x.DrawnAt != null, ct)).IsEqualTo(i + 1);
                var round = await StartRoundAsync(f, matchId, ct);
                var winner = i < opposingWins * 2 && i % 2 == 1 ? f.Right.Id : f.Left.Id;
                await WinAsync(f, matchId, round, winner, ct);
            }
            var result = (await store.FindAsync(f.Competition.Id, matchId, f.Owner.Id, true, f.Now, ct))!;
            await Assert.That(result.State).IsEqualTo(LiveSoloMatchState.Completed);
            await Assert.That(result.LeftWins).IsEqualTo(4);
            await Assert.That(result.RightWins).IsEqualTo(opposingWins);
            await Assert.That((await store.DrawAsync(f.Competition.Id, matchId, f.Owner.Id, f.Now, ct)).Failure).IsEqualTo(LiveSoloFailure.NotReady);
            await Assert.That(await f.Db.Set<LiveSoloRoundRosterMember>().CountAsync(ct)).IsEqualTo(rounds * 2);
            var configured = await f.Db.Set<LiveSoloMatchDirection>().Where(x => x.MatchId == matchId).ToArrayAsync(ct);
            await Assert.That(configured.Count(x => x.Direction == "Web")).IsEqualTo(2);
            await Assert.That(configured.Count(x => x.Direction == "Pwn")).IsEqualTo(2);
            await Assert.That(configured.Where(x => x.DrawnAt != null).Select(x => x.RoundNumber!.Value).Order().ToArray()).IsEquivalentTo(Enumerable.Range(1, rounds));
            await Assert.That(configured.Length).IsEqualTo(presets.Length);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Team_question_list_exclusivity_survives_unstarted_draws_and_invalid_configuration_is_atomic(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await Fixture.CreateAsync(ct, false);
            var (matchId, presets) = await SetupAsync(f, ct);
            var store = f.Store(f.Db);
            var duplicate = presets.ToArray(); duplicate[1] = duplicate[0];
            await Assert.That((await store.ConfigurePoolAsync(f.Competition.Id, matchId, f.Owner.Id, duplicate, ct)).Failure).IsEqualTo(LiveSoloFailure.InvalidConfiguration);
            await Assert.That(await f.Db.Set<LiveSoloMatchDirection>().CountAsync(ct)).IsEqualTo(7);
            await Assert.That((await store.DrawAsync(f.Competition.Id, matchId, f.Left.Id, f.Now, ct)).Failure).IsEqualTo(LiveSoloFailure.Forbidden);
            var draws = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
            {
                await using var context = new NoCtfDbContext(f.Options);
                return await f.Store(context).DrawAsync(f.Competition.Id, matchId, f.Owner.Id, f.Now, ct);
            }));
            await Assert.That(draws.Any(x => x.Failure == null)).IsTrue();
            f.Db.ChangeTracker.Clear();
            await Assert.That(await f.Db.Set<LiveSoloMatchDirection>().CountAsync(x => x.DrawnAt != null, ct)).IsEqualTo(1);
            var firstDraw = await f.Db.Set<LiveSoloMatchDirection>().SingleAsync(x => x.DrawnAt != null, ct);
            await Assert.That((await store.ConfigurePoolAsync(f.Competition.Id, matchId, f.Owner.Id, presets.Take(3).ToArray(), ct)).Failure).IsNull();
            await Assert.That(await f.Db.Set<LiveSoloMatchDirection>().AnyAsync(x => x.Id == firstDraw.Id && x.RoundNumber == 1, ct)).IsTrue();
            await Assert.That((await store.ConfigurePoolAsync(f.Competition.Id, matchId, f.Owner.Id, presets, ct)).Failure).IsNull();
            var old = await f.Db.LiveSoloMatches.SingleAsync(x => x.Id == matchId, ct);
            old.State = LiveSoloMatchState.Canceled;
            await f.Db.LiveSoloActiveTeamSlots.Where(x => x.MatchId == matchId).ExecuteDeleteAsync(ct);
            await f.Db.SaveChangesAsync(ct);
            var next = await store.CreateAsync(new(f.Competition.Id, f.Owner.Id, f.LeftTeam.Id, f.RightTeam.Id, 4, f.Now), ct);
            await Assert.That(next.Failure).IsNull();
            await Assert.That((await store.ConfigurePoolAsync(f.Competition.Id, next.Match!.Id, f.Owner.Id, presets, ct)).Failure).IsNull();
            var pool = (await store.DirectionsAsync(f.Competition.Id, next.Match.Id, f.Owner.Id, true, ct))!;
            await Assert.That(pool.Pool.Count(x => x.Available)).IsEqualTo(6);
            await Assert.That((await store.DrawAsync(f.Competition.Id, next.Match.Id, f.Owner.Id, f.Now, ct)).Failure).IsNull();
            var nextDraw = await f.Db.Set<LiveSoloMatchDirection>().SingleAsync(x => x.MatchId == next.Match.Id && x.DrawnAt != null, ct);
            await Assert.That(nextDraw.QuestionGroupId == firstDraw.QuestionGroupId).IsFalse();
            await Assert.That((await store.ConfigurePoolAsync(f.Competition.Id, next.Match.Id, f.Owner.Id, [], ct)).Failure).IsNull();
            await Assert.That(await f.Db.Set<LiveSoloMatchDirection>().CountAsync(x => x.MatchId == next.Match.Id, ct)).IsEqualTo(1);
            var preset = (await store.PresetsAsync(f.Competition.Id, f.Owner.Id, ct))!.Single(x => x.Id == presets[0]);
            var renamed = await store.SavePresetAsync(f.Competition.Id, f.Owner.Id, new(preset.Id, "", "Changed", preset.QuestionGroupId), ct);
            await Assert.That(renamed.Failure).IsNull();
            await Assert.That(renamed.Preset!.Name).IsEqualTo("Changed");
            await Assert.That(await store.DeletePresetAsync(f.Competition.Id, preset.Id, f.Owner.Id, ct)).IsNull();
            await Assert.That(await f.Db.Set<LiveSoloMatchDirection>().CountAsync(x => x.MatchId == matchId, ct)).IsEqualTo(7);
            await Assert.That(await f.Db.Set<LiveSoloMatchDirection>().AnyAsync(x => x.MatchId == matchId && x.Direction == "Changed", ct)).IsFalse();
        });
    }

    [Test, Timeout(300_000)]
    public async Task Rescue_requires_a_referee_adds_the_teammate_and_expires_at_the_next_round(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await Fixture.CreateAsync(ct, false);
            var (matchId, presets) = await SetupAsync(f, ct);
            var teammate = new User { Id = Guid.NewGuid(), UserName = "helper", NormalizedUserName = "HELPER", Email = "helper@example.test",
                PasswordHash = "unused", AccountStatus = UserAccountStatus.Active, CreatedAt = f.Now };
            var primary = new User { Id = Guid.NewGuid(), UserName = "primary", NormalizedUserName = "PRIMARY", Email = "primary@example.test",
                PasswordHash = "unused", AccountStatus = UserAccountStatus.Active, CreatedAt = f.Now };
            f.Db.Users.AddRange(teammate, primary); f.LeftTeam.MemberIds = [f.Left.Id, primary.Id, teammate.Id]; await f.Db.SaveChangesAsync(ct);
            var store = f.Store(f.Db);
            await store.DrawAsync(f.Competition.Id, matchId, f.Owner.Id, f.Now, ct);
            var round = await StartRoundAsync(f, matchId, ct, [f.Left.Id, primary.Id]);
            var drawId = round.DirectionDrawId;
            await Assert.That((await store.ConfigurePoolAsync(f.Competition.Id, matchId, f.Owner.Id, presets, ct)).Failure).IsNull();
            await Assert.That(await f.Db.Set<LiveSoloMatchDirection>().AnyAsync(x => x.Id == drawId, ct)).IsTrue();
            var question = round.Questions.Single();
            await VerifyOtherTeamsMayUseListAsync(f, round.QuestionGroupId, ct);
            var access = new LiveSoloExecutionAccess(f.Db);
            var request = new ExecutionScopeAccessRequest(question.Id, f.Competition.Id, question.CompetitionChallengeId, f.LeftTeam.Id, teammate.Id, ExecutionScopeOperation.Read, f.Now);
            await Assert.That(await access.CanAccessAsync(request, ct)).IsFalse();
            var command = new RequestLiveSoloRescue(f.Competition.Id, matchId, round.Id, teammate.Id, teammate.Id, "", f.Now);
            await Assert.That((await store.RequestRescueAsync(command, ct)).Failure).IsNull();
            await store.RequestRescueAsync(command, ct);
            var rescue = await f.Db.Set<LiveSoloRescueRequest>().SingleAsync(ct);
            var mine = (await store.DirectionsAsync(f.Competition.Id, matchId, f.Left.Id, false, ct))!;
            var referee = (await store.DirectionsAsync(f.Competition.Id, matchId, f.Owner.Id, true, ct))!;
            await Assert.That(mine.Rescues.Count).IsEqualTo(1);
            await Assert.That(mine.Pool.All(x => x.Name == x.Direction)).IsTrue();
            await Assert.That(mine.Pool.All(x => x.QuestionGroupId == null)).IsTrue();
            await Assert.That((await store.DirectionsAsync(f.Competition.Id, matchId, teammate.Id, false, ct))!.CanRequestRescue).IsTrue();
            await Assert.That(referee.CanJudgeRescue).IsTrue();
            await Assert.That((await store.DirectionsAsync(f.Competition.Id, matchId, f.Right.Id, false, ct))!.Rescues.Count).IsEqualTo(0);
            await Assert.That(await store.QuestionsAsync(f.Competition.Id, matchId, round.Id, teammate.Id, f.Now, ct)).IsNull();
            await Assert.That((await store.AdmitAsync(new(f.Competition.Id, matchId, round.Id, question.Id, teammate.Id, "flag{wrong}", f.Now), ct)).Failure).IsEqualTo(LiveSoloFailure.Forbidden);
            await Assert.That((await store.ResolveRescueAsync(new(f.Competition.Id, matchId, rescue.Id, f.Left.Id, true, "批准", f.Now), ct)).Failure).IsEqualTo(LiveSoloFailure.Forbidden);
            await Assert.That((await store.ResolveRescueAsync(new(f.Competition.Id, matchId, rescue.Id, f.Owner.Id, false, "", f.Now), ct)).Failure).IsNull();
            await Assert.That(await access.CanAccessAsync(request, ct)).IsFalse();
            await store.RequestRescueAsync(command, ct);
            rescue = await f.Db.Set<LiveSoloRescueRequest>().SingleAsync(x => x.State == LiveSoloRescueState.Pending, ct);
            var media = new LiveSoloMediaSession { Id = Guid.NewGuid(), MatchId = matchId, Generation = Guid.NewGuid(), RoomIdentity = Guid.NewGuid().ToString("N"),
                State = LiveSoloMediaState.Ready, CreatedAt = f.Now, Participants = [new() { UserId = f.Left.Id, TeamId = f.LeftTeam.Id, Identity = Guid.NewGuid().ToString("N") }] };
            f.Db.Add(media); (await f.Db.LiveSoloMatches.SingleAsync(x => x.Id == matchId, ct)).CurrentMediaSessionId = media.Id; await f.Db.SaveChangesAsync(ct);
            await Assert.That((await store.ResolveRescueAsync(new(f.Competition.Id, matchId, rescue.Id, f.Owner.Id, true, "", f.Now), ct)).Failure).IsNull();
            await Assert.That(await access.CanAccessAsync(request, ct)).IsTrue();
            await Assert.That((await store.QuestionsAsync(f.Competition.Id, matchId, round.Id, teammate.Id, f.Now, ct))!.Count).IsEqualTo(1);
            var current = (await store.FindAsync(f.Competition.Id, matchId, f.Owner.Id, true, f.Now, ct))!;
            await Assert.That(current.Rosters.Single(x => x.TeamId == f.LeftTeam.Id).UserIds).IsEquivalentTo(new[] { f.Left.Id, primary.Id, teammate.Id });
            await Assert.That(await f.Db.Set<LiveSoloRoundRosterMember>().CountAsync(x => x.RoundId == round.Id, ct)).IsEqualTo(4);
            await Assert.That(media.Participants.Any(x => x.UserId == teammate.Id)).IsTrue();
            await WinAsync(f, matchId, round, teammate.Id, ct);
            await store.DrawAsync(f.Competition.Id, matchId, f.Owner.Id, f.Now, ct);
            await Assert.That(await access.CanAccessAsync(request, ct)).IsFalse();
            await Assert.That((await store.ResolveRescueAsync(new(f.Competition.Id, matchId, rescue.Id, f.Owner.Id, true, "重复批准", f.Now), ct)).Failure).IsNull();
            current = (await store.FindAsync(f.Competition.Id, matchId, f.Owner.Id, true, f.Now, ct))!;
            await Assert.That(current.Rosters.All(x => x.UserIds.Count == 0)).IsTrue();
            await Assert.That(media.State).IsEqualTo(LiveSoloMediaState.Stopping);
            var following = await StartRoundAsync(f, matchId, ct);
            var review = new LiveSoloReviewStore(f.Db, new CompetitionModerationAuthorizer(f.Db));
            var reviewRequest = new LiveSoloReviewRequest(f.Competition.Id, matchId, teammate.Id, false, f.Now);
            await Assert.That(await review.ReadAsync(reviewRequest, question.Id, ct)).IsNotNull();
            await Assert.That(await review.ReadAsync(reviewRequest, following.Questions.Single().Id, ct)).IsNull();
            await Assert.That((await review.ListAsync(reviewRequest, ct))!.Select(x => x.RoundId)).IsEquivalentTo(new[] { round.Id });
        });
    }

    [Test, Timeout(300_000)]
    public async Task Rescue_works_without_a_direction_pool_and_only_grants_the_current_round(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await Fixture.CreateAsync(ct, false);
            ((LiveSoloCompetitionModeConfiguration)f.Competition.ModeConfiguration!).RequiredWins = 2;
            await f.Db.SaveChangesAsync(ct); await f.PrepareAsync(ct); await f.StartAsync(ct);
            var helper = new User { Id = Guid.NewGuid(), UserName = "helper", NormalizedUserName = "HELPER", Email = "helper@example.test",
                PasswordHash = "unused", AccountStatus = UserAccountStatus.Active, CreatedAt = f.Now };
            f.Db.Add(helper); f.LeftTeam.MemberIds = [f.Left.Id, helper.Id]; await f.Db.SaveChangesAsync(ct);
            var store = f.Store(f.Db);
            await Assert.That((await store.DirectionsAsync(f.Competition.Id, f.Match.Id, helper.Id, false, ct))!.CanRequestRescue).IsTrue();
            await Assert.That((await store.RequestRescueAsync(new(f.Competition.Id, f.Match.Id, f.Round.Id, helper.Id, helper.Id, "", f.Now), ct)).Failure).IsNull();
            var rescue = await f.Db.Set<LiveSoloRescueRequest>().SingleAsync(ct);
            await Assert.That((await store.ResolveRescueAsync(new(f.Competition.Id, f.Match.Id, rescue.Id, f.Owner.Id, true, "", f.Now), ct)).Failure).IsNull();
            var question = f.Round.Questions.Single(x => x.Position == 0);
            await Assert.That((await store.AdmitAsync(new(f.Competition.Id, f.Match.Id, f.Round.Id, question.Id, helper.Id, "flag{first}", f.Now), ct)).Failure).IsNull();
            await store.ResolveAsync(f.Round.Id, f.Now, ct);
            var group = (await store.SaveGroupAsync(f.Competition.Id, f.Owner.Id, new(null, "Following list", false, null, [new(f.Entries[1].Id, 0)], null), f.Now, ct)).Group!;
            var candidate = (await store.CandidatesAsync(f.Competition.Id, f.Match.Id, f.Owner.Id, f.Now, ct))!.Single(x => x.Id == group.Id);
            var match = (await store.FindAsync(f.Competition.Id, f.Match.Id, f.Owner.Id, true, f.Now, ct))!;
            var next = await store.PrepareRoundAsync(new(f.Competition.Id, f.Match.Id, f.Owner.Id, match.ConcurrencyStamp, group.Id, f.Now,
                candidate.ConcurrencyStamp, candidate.SelectionRevision), ct);
            await Assert.That(next.Failure).IsNull();
            match = (await store.FindAsync(f.Competition.Id, f.Match.Id, f.Owner.Id, true, f.Now, ct))!;
            await Assert.That(match.Rosters.SelectMany(x => x.UserIds).Contains(helper.Id)).IsFalse();
            await Assert.That(await f.Db.Set<LiveSoloRoundRosterMember>().AnyAsync(x => x.RoundId == next.Round!.Id && x.UserId == helper.Id, ct)).IsFalse();
        });
    }

    private static async Task<(Guid MatchId, Guid[] Presets)> SetupAsync(Fixture f, CancellationToken ct)
    {
        var store = f.Store(f.Db);
        var created = await store.CreateAsync(new(f.Competition.Id, f.Owner.Id, f.LeftTeam.Id, f.RightTeam.Id, 4, f.Now), ct);
        await Assert.That(created.Failure).IsNull();
        string[] directions = ["Web", "Web", "Pwn", "Pwn", "Crypto", "Reverse", "Misc"];
        var presets = new List<Guid>();
        for (var i = 0; i < directions.Length; i++)
        {
            var template = new LiveSoloChallenge { Id = Guid.NewGuid(), OwnerId = f.Owner.Id, Title = "Draw question " + i, Direction = directions[i],
                Definition = new LiveSoloChallengeDefinition(), CreatedAt = f.Now, UpdatedAt = f.Now };
            var entry = new LiveSoloCompetitionChallenge { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, ChallengeId = template.Id,
                Rules = new LiveSoloCompetitionChallengeRules(), Order = i + 2, UpdatedAt = f.Now };
            f.Db.Add(template); f.Db.Add(entry);
            f.Db.Add(new TemplateChallengeFlag { Id = Guid.NewGuid(), ChallengeId = template.Id, Flag = "flag{" + i + "}",
                FlagSha256 = ManageChallengeFlags.Hash("flag{" + i + "}"), CreatedAt = f.Now });
            await f.Db.SaveChangesAsync(ct);
            var group = await store.SaveGroupAsync(f.Competition.Id, f.Owner.Id, new(null, "List " + i, false, null, [new(entry.Id, 0)], null), f.Now, ct);
            await Assert.That(group.Failure).IsNull();
            var preset = await store.SavePresetAsync(f.Competition.Id, f.Owner.Id, new(null, directions[i] + i, directions[i], group.Group!.Id), ct);
            await Assert.That(preset.Failure).IsNull(); presets.Add(preset.Preset!.Id);
        }
        await Assert.That((await store.ConfigurePoolAsync(f.Competition.Id, created.Match!.Id, f.Owner.Id, presets, ct)).Failure).IsNull();
        return (created.Match.Id, presets.ToArray());
    }

    private static async Task<LiveSoloRound> StartRoundAsync(Fixture f, Guid matchId, CancellationToken ct, IReadOnlyList<Guid>? leftRoster = null)
    {
        var store = f.Store(f.Db);
        var match = (await store.FindAsync(f.Competition.Id, matchId, f.Owner.Id, true, f.Now, ct))!;
        var left = await store.LockRosterAsync(new(f.Competition.Id, matchId, f.Left.Id, f.LeftTeam.Id, match.ConcurrencyStamp, leftRoster ?? [f.Left.Id], f.Now), ct);
        await Assert.That(left.Failure).IsNull();
        var right = await store.LockRosterAsync(new(f.Competition.Id, matchId, f.Right.Id, f.RightTeam.Id, left.Match!.ConcurrencyStamp, [f.Right.Id], f.Now), ct);
        await Assert.That(right.Failure).IsNull();
        var prepared = await store.PrepareRoundAsync(new(f.Competition.Id, matchId, f.Owner.Id, right.Match!.ConcurrencyStamp, null, f.Now), ct);
        await Assert.That(prepared.Failure).IsNull();
        var round = await f.Db.LiveSoloRounds.Include(x => x.Questions).SingleAsync(x => x.Id == prepared.Round!.Id, ct);
        await Assert.That(await store.QuestionsAsync(f.Competition.Id, matchId, round.Id, f.Left.Id, f.Now, ct)).IsNull();
        match = (await store.FindAsync(f.Competition.Id, matchId, f.Owner.Id, true, f.Now, ct))!;
        left = await store.ConfirmReadyAsync(f.Competition.Id, matchId, f.Left.Id, match.ConcurrencyStamp, f.Now, ct);
        right = await store.ConfirmReadyAsync(f.Competition.Id, matchId, f.Right.Id, left.Match!.ConcurrencyStamp, f.Now, ct);
        await Assert.That(right.Failure).IsNull();
        var countdown = await store.StartCountdownAsync(new(f.Competition.Id, matchId, round.Id, f.Owner.Id, round.ConcurrencyStamp, f.Now), ct);
        await Assert.That(countdown.Failure).IsNull();
        f.Now = f.Now.AddSeconds(round.CountdownSeconds);
        await store.TickAsync(round.Id, countdown.Round!.TimelineRevision, f.Now, ct);
        await Assert.That(round.State).IsEqualTo(LiveSoloRoundState.Running);
        return round;
    }

    private static async Task WinAsync(Fixture f, Guid matchId, LiveSoloRound round, Guid actor, CancellationToken ct)
    {
        var question = round.Questions.Single();
        var template = await f.Db.CompetitionChallenges.Where(x => x.Id == question.CompetitionChallengeId).Select(x => x.ChallengeId).SingleAsync(ct);
        var flag = await f.Db.ChallengeFlags.Where(x => x.ChallengeId == template).Select(x => x.Flag).SingleAsync(ct);
        var store = f.Store(f.Db);
        var admission = await store.AdmitAsync(new(f.Competition.Id, matchId, round.Id, question.Id, actor, flag, f.Now), ct);
        await Assert.That(admission.Failure).IsNull();
        await store.ResolveAsync(round.Id, f.Now, ct);
        await Assert.That(round.State).IsEqualTo(LiveSoloRoundState.Won);
    }

    private static async Task VerifyOtherTeamsMayUseListAsync(Fixture f, Guid groupId, CancellationToken ct)
    {
        var players = Enumerable.Range(0, 2).Select(i => new User { Id = Guid.NewGuid(), UserName = "other" + i, NormalizedUserName = "OTHER" + i,
            Email = "other" + i + "@example.test", PasswordHash = "unused", AccountStatus = UserAccountStatus.Active, CreatedAt = f.Now }).ToArray();
        var teams = players.Select((player, i) => new NoCTF.Domain.Teams.Team { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id,
            Name = player.UserName, CaptainId = player.Id, MemberIds = [player.Id], InvitationToken = new string(i == 0 ? 'a' : 'b', 32),
            RegistrationStatus = NoCTF.Domain.Teams.TeamRegistrationStatus.Approved, RegisteredAt = f.Now }).ToArray();
        f.Db.AddRange(players); f.Db.AddRange(teams); await f.Db.SaveChangesAsync(ct);
        var store = f.Store(f.Db);
        var match = (await store.CreateAsync(new(f.Competition.Id, f.Owner.Id, teams[0].Id, teams[1].Id, 1, f.Now), ct)).Match!;
        var preset = (await store.PresetsAsync(f.Competition.Id, f.Owner.Id, ct))!.Single(x => x.QuestionGroupId == groupId);
        await Assert.That((await store.ConfigurePoolAsync(f.Competition.Id, match.Id, f.Owner.Id, [preset.Id], ct)).Failure).IsNull();
        match = (await store.DrawAsync(f.Competition.Id, match.Id, f.Owner.Id, f.Now, ct)).Match!;
        for (var i = 0; i < players.Length; i++)
        {
            var roster = await store.LockRosterAsync(new(f.Competition.Id, match.Id, players[i].Id, teams[i].Id, match.ConcurrencyStamp, [players[i].Id], f.Now), ct);
            await Assert.That(roster.Failure).IsNull(); match = roster.Match!;
        }
        var prepared = await store.PrepareRoundAsync(new(f.Competition.Id, match.Id, f.Owner.Id, match.ConcurrencyStamp, null, f.Now), ct);
        await Assert.That(prepared.Failure).IsNull();
        await Assert.That(prepared.Round!.QuestionGroupId).IsEqualTo(groupId);
    }
}
