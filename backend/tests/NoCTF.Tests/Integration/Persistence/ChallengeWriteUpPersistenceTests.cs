using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.WriteUps;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
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
}
