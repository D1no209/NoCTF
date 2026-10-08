using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using NSubstitute;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.WriteUps;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Gameplay;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Notifications;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NoCTF.Infrastructure.Caching;
using ZiggyCreatures.Caching.Fusion;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Challenges.WriteUps;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeWriteUpPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Draft_submission_publication_and_rejection_keep_old_public_content_private_and_immutable(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_challenge_writeups").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var owner = User("writeup-owner", now);
            var author = User("writeup-author", now);
            var reader = User("writeup-reader", now);
            var readerMember = User("writeup-reader-member", now);
            var competition = new CtfCompetition { Id = Guid.NewGuid(), OwnerId = owner.Id, Title = "Single WriteUps",
                ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf), FlagDerivationSecret = new byte[32],
                StartAt = now.AddHours(-1), EndAt = now.AddHours(1), Status = CompetitionStatus.Running,
                SingleWriteUpsEnabled = true, CreatedAt = now, UpdatedAt = now };
            var template = new CtfChallenge { Id = Guid.NewGuid(), OwnerId = owner.Id, Title = "WriteUp source", Direction = "Web",
                Definition = TestConfigurations.Definition(GameMode.Ctf), CreatedAt = now, UpdatedAt = now };
            var challenge = new CtfCompetitionChallenge { Id = Guid.NewGuid(), CompetitionId = competition.Id, ChallengeId = template.Id,
                IsPublished = true, Rules = TestConfigurations.Rules(GameMode.Ctf), UpdatedAt = now };
            db.Users.AddRange(owner, author, reader, readerMember); db.Competitions.Add(competition); db.Challenges.Add(template);
            var readerTeam = Team(reader.Id, competition.Id, 'b', now); readerTeam.MemberIds = [reader.Id, readerMember.Id];
            db.CompetitionChallenges.Add(challenge); db.Teams.AddRange(Team(author.Id, competition.Id, 'a', now), readerTeam);
            await db.SaveChangesAsync(ct);
            var store = new ChallengeWriteUpStore(db, new CompetitionModerationAuthorizer(db), new CompetitionChallengeReadAccess(db),
                NullCompetitionEventRecorder.Instance, Substitute.For<IPostCommitMessagePublisher>());
            var save = new SaveWriteUpDraft(competition.Id, challenge.Id, author.Id, false, WriteUpFormat.Markdown, "# old secret", null, null, now);
            var draft = await store.SaveDraftAsync(save, ct);
            await Assert.That(draft.Failure).IsNull();
            var submitted = await store.SubmitAsync(new(competition.Id, challenge.Id, author.Id, false, draft.WriteUp!.ConcurrencyStamp, now), ct);
            var v1 = submitted.WriteUp!.Submitted!.Id;
            var published = await store.ReviewAsync(new(competition.Id, challenge.Id, submitted.WriteUp.Id, v1, owner.Id,
                submitted.WriteUp.ConcurrencyStamp, WriteUpReviewAction.Publish, null, now), ct);
            await Assert.That(published.Failure).IsNull();
            var listed = await store.ListAsync(competition.Id, challenge.Id, reader.Id, false, now, ct);
            await Assert.That(listed!.Items.Single().Published!.Markdown).IsNull();
            await Assert.That(listed.Items.Single().Versions).IsEmpty();
            var refused = await store.ReadContentAsync(competition.Id, challenge.Id, v1, reader.Id, false, now, ct);
            await Assert.That(refused.Failure).IsEqualTo(ChallengeWriteUpFailure.Forbidden);
            var own = await store.ReadContentAsync(competition.Id, challenge.Id, v1, author.Id, false, now, ct);
            await Assert.That(own.Markdown).IsEqualTo("# old secret");
            var unlock = new UnlockWriteUp(competition.Id, challenge.Id, v1, reader.Id, listed.Access.Settings.PolicyStamp, now);
            var changed = await store.UnlockAsync(unlock with { PolicyStamp = Guid.NewGuid() }, ct);
            await Assert.That(changed.Failure).IsEqualTo(ChallengeWriteUpFailure.ConfirmationChanged);
            var parallel = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
            {
                await using var parallelDb = new NoCtfDbContext(options);
                var parallelStore = new ChallengeWriteUpStore(parallelDb, new CompetitionModerationAuthorizer(parallelDb),
                    new CompetitionChallengeReadAccess(parallelDb), NullCompetitionEventRecorder.Instance,
                    Substitute.For<IPostCommitMessagePublisher>());
                return await parallelStore.UnlockAsync(unlock with { ActorId = i % 2 == 0 ? reader.Id : readerMember.Id }, ct);
            }));
            await Assert.That(parallel.Count(x => x.Created)).IsEqualTo(1);
            await Assert.That(parallel.All(x => x.Failure is null)).IsTrue();
            await Assert.That(await db.WriteUpUnlockReceipts.CountAsync(ct)).IsEqualTo(1);
            var shared = await store.ReadContentAsync(competition.Id, challenge.Id, v1, readerMember.Id, false, now, ct);
            await Assert.That(shared.Markdown).IsEqualTo("# old secret");
            var second = await store.SaveDraftAsync(save with { Markdown = "# new secret", ExpectedStamp = published.WriteUp!.ConcurrencyStamp }, ct);
            var secondSubmit = await store.SubmitAsync(new(competition.Id, challenge.Id, author.Id, false, second.WriteUp!.ConcurrencyStamp, now), ct);
            await Assert.That(secondSubmit.WriteUp!.PublishedVersionId).IsEqualTo(v1);
            var rejected = await store.ReviewAsync(new(competition.Id, challenge.Id, secondSubmit.WriteUp.Id, secondSubmit.WriteUp.Submitted!.Id,
                owner.Id, secondSubmit.WriteUp.ConcurrencyStamp, WriteUpReviewAction.Reject, "Needs explanation", now), ct);
            await Assert.That(rejected.WriteUp!.PublishedVersionId).IsEqualTo(v1);
            await Assert.That(rejected.WriteUp.Submitted!.State).IsEqualTo(WriteUpVersionState.Rejected);
            var stale = await store.SaveDraftAsync(save with { ExpectedStamp = draft.WriteUp.ConcurrencyStamp }, ct);
            await Assert.That(stale.Failure).IsEqualTo(ChallengeWriteUpFailure.Conflict);
            competition.Status = CompetitionStatus.Finished; await db.SaveChangesAsync(ct);
            var free = await store.ReadContentAsync(competition.Id, challenge.Id, v1, reader.Id, false, now, ct);
            await Assert.That(free.Markdown).IsEqualTo("# old secret");
            await Assert.That(await db.WriteUpUnlockReceipts.CountAsync(ct)).IsEqualTo(1);
            competition.SingleWriteUpsEnabled = false; await db.SaveChangesAsync(ct);
            await Assert.That(await store.ListAsync(competition.Id, challenge.Id, reader.Id, false, now, ct)).IsNull();
            await Assert.That(await store.ListAsync(competition.Id, challenge.Id, owner.Id, true, now, ct)).IsNotNull();
            var version = await db.ChallengeWriteUpVersions.SingleAsync(x => x.Id == v1, ct);
            version.Markdown = "tampered";
            await Assert.That(async () => await db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
        });
    }

    private static User User(string name, DateTimeOffset now) => new() { Id = Guid.NewGuid(), UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test", PasswordHash = "unused", AccountStatus = UserAccountStatus.Active, CreatedAt = now };
    private static Team Team(Guid member, Guid competitionId, char key, DateTimeOffset now) => new() {
        Id = Guid.NewGuid(), CompetitionId = competitionId, Name = $"Team {key}", CaptainId = member, MemberIds = [member],
        InvitationToken = new string(key, 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = now };

    [Test, Timeout(300_000)]
    public async Task Receipts_are_scope_isolated_snapshot_policies_survive_withdrawal_and_rollbacks_leave_no_access(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var commitFailure = new FailCommit();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().AddInterceptors(commitFailure).Options;
            await using var db = new NoCtfDbContext(options); await db.Database.EnsureCreatedAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var owner = User("scope-owner", now); var author = User("scope-author", now); var reader = User("scope-reader", now);
            CtfCompetition Competition(string name) => new() { Id = Guid.NewGuid(), OwnerId = owner.Id, Title = name,
                ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf), FlagDerivationSecret = new byte[32],
                StartAt = now.AddHours(-1), EndAt = now.AddHours(1), Status = CompetitionStatus.Running,
                SingleWriteUpsEnabled = true, CreatedAt = now, UpdatedAt = now };
            var a = Competition("A"); var b = Competition("B");
            var template = new CtfChallenge { Id = Guid.NewGuid(), OwnerId = owner.Id, Title = "shared template", Direction = "Web",
                Definition = TestConfigurations.Definition(GameMode.Ctf), CreatedAt = now, UpdatedAt = now };
            var secondTemplate = new CtfChallenge { Id = Guid.NewGuid(), OwnerId = owner.Id, Title = "other template", Direction = "Web",
                Definition = TestConfigurations.Definition(GameMode.Ctf), CreatedAt = now, UpdatedAt = now };
            CtfCompetitionChallenge Challenge(Guid competitionId) => new() { Id = Guid.NewGuid(), CompetitionId = competitionId,
                ChallengeId = template.Id, IsPublished = true, Rules = TestConfigurations.Rules(GameMode.Ctf), UpdatedAt = now };
            var first = Challenge(a.Id); var second = Challenge(a.Id); var otherCompetition = Challenge(b.Id);
            second.ChallengeId = secondTemplate.Id;
            second.Order = 1;
            var authorA = Team(author.Id, a.Id, 'a', now); var readerA = Team(reader.Id, a.Id, 'b', now);
            db.Users.AddRange(owner, author, reader); db.Competitions.AddRange(a, b); db.Challenges.AddRange(template, secondTemplate);
            db.CompetitionChallenges.AddRange(first, second, otherCompetition);
            db.Teams.AddRange(authorA, readerA, Team(author.Id, b.Id, 'c', now), Team(reader.Id, b.Id, 'd', now));
            await db.SaveChangesAsync(ct);
            ChallengeWriteUpStore Store(NoCtfDbContext context) => new(context, new CompetitionModerationAuthorizer(context),
                new CompetitionChallengeReadAccess(context), NullCompetitionEventRecorder.Instance, Substitute.For<IPostCommitMessagePublisher>());
            var store = Store(db);
            async Task<ChallengeWriteUpView> Publish(CtfCompetitionChallenge challenge)
            {
                var draft = await store.SaveDraftAsync(new(challenge.CompetitionId, challenge.Id, author.Id, false,
                    WriteUpFormat.Markdown, "# scoped solution", null, null, now), ct);
                var submitted = await store.SubmitAsync(new(challenge.CompetitionId, challenge.Id, author.Id, false, draft.WriteUp!.ConcurrencyStamp, now), ct);
                return (await store.ReviewAsync(new(challenge.CompetitionId, challenge.Id, submitted.WriteUp!.Id,
                    submitted.WriteUp.Submitted!.Id, owner.Id, submitted.WriteUp.ConcurrencyStamp, WriteUpReviewAction.Publish, null, now), ct)).WriteUp!;
            }
            var one = await Publish(first); var two = await Publish(second); var three = await Publish(otherCompetition);
            async Task<UnlockWriteUp> Command(CtfCompetitionChallenge challenge, Guid version) => new(challenge.CompetitionId,
                challenge.Id, version, reader.Id, (await store.ListAsync(challenge.CompetitionId, challenge.Id, reader.Id, false, now, ct))!.Access.Settings.PolicyStamp, now);
            await store.UnlockAsync(await Command(first, one.PublishedVersionId!.Value), ct);
            await Assert.That((await store.ReadContentAsync(a.Id, second.Id, two.PublishedVersionId!.Value, reader.Id, false, now, ct)).Failure).IsEqualTo(ChallengeWriteUpFailure.Forbidden);
            await Assert.That((await store.ReadContentAsync(b.Id, otherCompetition.Id, three.PublishedVersionId!.Value, reader.Id, false, now, ct)).Failure).IsEqualTo(ChallengeWriteUpFailure.Forbidden);
            await Assert.That((await store.ReadContentAsync(a.Id, first.Id, one.PublishedVersionId.Value, author.Id, false, now, ct)).Failure).IsNull();
            var policy = await store.GetSettingsAsync(a.Id, null, owner.Id, now, ct);
            var updated = await store.UpdateSettingsAsync(new(a.Id, null, owner.Id, policy.ConcurrencyStamp!.Value, null, 80, null, true, now), ct);
            await Assert.That(updated.Failure).IsNull();
            await Assert.That((await store.ListAsync(a.Id, first.Id, reader.Id, false, now, ct))!.Access.DeductionPercent).IsEqualTo(20);
            var withdrawn = await store.ReviewAsync(new(a.Id, first.Id, one.Id, one.PublishedVersionId.Value, owner.Id,
                one.ConcurrencyStamp, WriteUpReviewAction.Withdraw, null, now), ct);
            await Assert.That((await store.ReadContentAsync(a.Id, first.Id, one.PublishedVersionId.Value, reader.Id, false, now, ct)).Failure).IsNotNull();
            await store.ReviewAsync(new(a.Id, first.Id, one.Id, one.PublishedVersionId.Value, owner.Id,
                withdrawn.WriteUp!.ConcurrencyStamp, WriteUpReviewAction.Publish, null, now), ct);
            await Assert.That((await store.UnlockAsync(await Command(first, one.PublishedVersionId.Value), ct)).Created).IsFalse();
            var failedCommand = await Command(second, two.PublishedVersionId.Value);
            commitFailure.Armed = true;
            await Assert.That(async () => await store.UnlockAsync(failedCommand, ct)).Throws<InvalidOperationException>();
            db.ChangeTracker.Clear();
            await Assert.That(await db.WriteUpUnlockReceipts.CountAsync(ct)).IsEqualTo(1);
            await Assert.That(await db.GameplayFacts.CountAsync(x => x.Kind == GameplayFactKind.WriteUpUnlock, ct)).IsEqualTo(1);
            await Assert.That((await store.ReadContentAsync(a.Id, second.Id, two.PublishedVersionId.Value, reader.Id, false, now, ct)).Failure).IsEqualTo(ChallengeWriteUpFailure.Forbidden);
            db.GameplayFacts.Add(new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = a.Id,
                CompetitionChallengeId = first.Id, TeamId = readerA.Id, ActorUserId = reader.Id, State = GameplayFactState.Completed,
                Result = GameplayFactResult.Correct, OccurredAt = now.AddSeconds(1), UpdatedAt = now.AddSeconds(1) });
            await db.SaveChangesAsync(ct);
            using var services = new ServiceCollection().AddFusionCache(NoCtfCacheNames.Leaderboards).Services.BuildServiceProvider();
            var factory = new FusionLeaderboardCache(db, new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(), services.GetRequiredService<IFusionCacheProvider>());
            var projection = (await factory.CreateScoreboardAsync(a.Id, now.AddMinutes(1), ct))!;
            var row = projection.Snapshot.Teams.Single(x => x.TeamId == readerA.Id); var benefit = row.ChallengeBenefits.Single(x => x.CompetitionChallengeId == first.Id);
            await Assert.That(benefit.WriteUpDeductionPercent).IsEqualTo(20);
            await Assert.That(row.TotalScore).IsEqualTo(benefit.NetPoints);
            await Assert.That(benefit.NetPoints).IsEqualTo(benefit.GrossPoints - benefit.WriteUpDeductionPoints);
            await Assert.That(row.Slots.SelectMany(x => x.Entries).Any(x => x.Award is not null)).IsFalse();
        });
    }

    private sealed class FailCommit : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed) { Armed = false; throw new InvalidOperationException("Injected writeup commit failure"); }
            return ValueTask.FromResult(result);
        }
    }
}
