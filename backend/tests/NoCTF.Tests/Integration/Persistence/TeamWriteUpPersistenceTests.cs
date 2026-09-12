using System.Security.Cryptography;
using FluentStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Challenges.Questions;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Storage;
using NoCTF.Application.Teams.WriteUps;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Challenges.Questions;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Storage;
using NoCTF.Infrastructure.Teams.WriteUps;
using NoCTF.Worker;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class TeamWriteUpPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Team_members_replace_audited_pdf_while_other_users_are_rejected(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_team_writeups")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var storageRoot = Path.Combine(
                Path.GetTempPath(),
                $"noctf-team-writeup-tests-{Guid.NewGuid():N}");
            try
            {
                using var storage = StorageFactory.Disk(storageRoot);
                var now = DateTimeOffset.Parse("2026-09-12T15:00:00Z");
                var ownerId = Guid.CreateVersion7(now);
                var memberId = Guid.CreateVersion7(now.AddTicks(1));
                var outsiderId = Guid.CreateVersion7(now.AddTicks(2));
                var competitionId = Guid.CreateVersion7(now.AddTicks(3));
                var teamId = Guid.CreateVersion7(now.AddTicks(4));
                var outbox = new RecordingOutbox();

                await using var db = new NoCtfDbContext(options);
                await db.Database.EnsureCreatedAsync(cancellationToken);
                db.Users.AddRange(
                    User(ownerId, "writeup-owner", now),
                    User(memberId, "writeup-member", now),
                    User(outsiderId, "writeup-outsider", now));
                db.Competitions.Add(new Competition
                {
                    Id = competitionId,
                    OwnerId = ownerId,
                    Title = "WriteUp competition",
                    Mode = GameMode.Ctf,
                    Status = CompetitionStatus.Finished,
                    ConfigurationJson = "{}",
                    FlagDerivationSecret = new byte[32],
                    StartAt = now.AddHours(-2),
                    EndAt = now.AddHours(-1),
                    CreatedAt = now.AddHours(-3),
                    UpdatedAt = now
                });
                db.Teams.Add(new Team
                {
                    Id = teamId,
                    CompetitionId = competitionId,
                    Name = "WriteUp team",
                    CaptainId = ownerId,
                    MemberIds = [ownerId, memberId],
                    InvitationToken = "0123456789abcdefghijklmnopqrstuv",
                    RegistrationStatus = TeamRegistrationStatus.Approved,
                    RegisteredAt = now.AddHours(-2)
                });
                await db.SaveChangesAsync(cancellationToken);

                var registry = new ManagedFileUploadRegistry(
                    db,
                    outbox,
                    NullLogger<ManagedFileUploadRegistry>.Instance);
                var manager = new ManageTeamWriteUps(
                    new TeamWriteUpStore(
                        db,
                        outbox,
                        new CompetitionEventStore(db, outbox),
                        new FileReferenceLock()),
                    new ManagedFileUploads(registry, storage),
                    storage,
                    Substitute.For<ILeaderboardCache>());

                var emptyReview = await manager.ReviewAsync(
                    competitionId,
                    cancellationToken);
                await Assert.That(emptyReview.ScoreboardAvailable).IsFalse();
                await Assert.That(emptyReview.Items).IsEmpty();

                await using var outsiderPdf = Pdf("outsider");
                var rejected = await manager.ReplaceMineAsync(
                    competitionId,
                    outsiderId,
                    "outsider.pdf",
                    TeamWriteUpRules.ContentType,
                    outsiderPdf.Length,
                    outsiderPdf,
                    now,
                    cancellationToken);
                await Assert.That(rejected.State)
                    .IsEqualTo(TeamWriteUpSubmissionState.Forbidden);
                await Assert.That(await db.Files.CountAsync(cancellationToken)).IsEqualTo(0);

                await using var firstPdf = Pdf("first");
                var first = await manager.ReplaceMineAsync(
                    competitionId,
                    memberId,
                    "team-writeup.pdf",
                    TeamWriteUpRules.ContentType,
                    firstPdf.Length,
                    firstPdf,
                    now.AddMinutes(1),
                    cancellationToken);
                await Assert.That(first.State).IsEqualTo(TeamWriteUpSubmissionState.Updated);

                await using var secondPdf = Pdf("second");
                var second = await manager.ReplaceMineAsync(
                    competitionId,
                    ownerId,
                    "updated-writeup.pdf",
                    TeamWriteUpRules.ContentType,
                    secondPdf.Length,
                    secondPdf,
                    now.AddMinutes(2),
                    cancellationToken);
                await Assert.That(second.State).IsEqualTo(TeamWriteUpSubmissionState.Updated);
                await Assert.That(second.WriteUp!.TeamId).IsEqualTo(teamId);
                await Assert.That(second.WriteUp.SubmittedByUserId).IsEqualTo(ownerId);
                await Assert.That(second.WriteUp.Sha256)
                    .IsEqualTo(Convert.ToHexString(SHA256.HashData(PdfBytes("second"))));

                var persisted = await db.Teams.AsNoTracking()
                    .SingleAsync(team => team.Id == teamId, cancellationToken);
                await Assert.That(persisted.WriteUpFileId).IsEqualTo(second.WriteUp.FileId);
                await Assert.That(persisted.WriteUpSubmittedByUserId).IsEqualTo(ownerId);
                await Assert.That(persisted.WriteUpSubmittedAt).IsEqualTo(now.AddMinutes(2));
                await Assert.That(await db.CompetitionEvents.CountAsync(@event =>
                    @event.CompetitionId == competitionId
                    && @event.Kind == CompetitionEventKind.TeamWriteUpSubmitted,
                    cancellationToken)).IsEqualTo(2);
                await Assert.That(outbox.Published.OfType<CleanupFile>()
                        .Any(message => message.FileId == first.WriteUp!.FileId))
                    .IsTrue();

                await BackendMessageOperations.CleanupFileAsync(
                    new CleanupFile(first.WriteUp!.FileId),
                    db,
                    storage,
                    cancellationToken);
                await Assert.That(await db.Files.AnyAsync(file =>
                    file.Id == first.WriteUp.FileId,
                    cancellationToken)).IsFalse();
                await Assert.That(await db.Files.AnyAsync(file =>
                    file.Id == second.WriteUp.FileId,
                    cancellationToken)).IsTrue();

                var opened = await manager.OpenMineAsync(
                    competitionId,
                    memberId,
                    cancellationToken);
                await Assert.That(opened).IsNotNull();
                await using var openedContent = opened!.Content;
                using var reader = new StreamReader(openedContent);
                await Assert.That(await reader.ReadToEndAsync(cancellationToken))
                    .Contains("second");

                var staffOpened = await manager.OpenAsync(
                    competitionId,
                    teamId,
                    cancellationToken);
                await Assert.That(staffOpened).IsNotNull();
                await using var staffContent = staffOpened!.Content;
                await Assert.That(staffOpened.Metadata.FileId)
                    .IsEqualTo(second.WriteUp.FileId);

                var questions = new CompetitionQuestionStore(
                    db,
                    outbox,
                    new CompetitionEventStore(db, outbox),
                    NullLogger<CompetitionQuestionStore>.Instance);
                var consultation = await questions.CreateWriteUpConsultationAsync(new(
                    competitionId,
                    teamId,
                    null,
                    ownerId,
                    "WriteUp review",
                    "Please clarify the scoring evidence in this WriteUp.",
                    now.AddMinutes(3)), cancellationToken);
                await Assert.That(consultation.Failure).IsNull();
                await Assert.That(consultation.Question!.Status)
                    .IsEqualTo(CompetitionQuestionStatus.Replied);
                await Assert.That(consultation.Question.Entries[0].ActorRole)
                    .IsEqualTo(CompetitionQuestionParticipantRole.CompetitionManager);
                await Assert.That(await questions.FindAsync(
                    competitionId,
                    consultation.Question.ThreadRootId,
                    memberId,
                    cancellationToken)).IsNotNull();
                await Assert.That(await questions.FindAsync(
                    competitionId,
                    consultation.Question.ThreadRootId,
                    outsiderId,
                    cancellationToken)).IsNull();

                var participantReply = await questions.AddMessageAsync(new(
                    competitionId,
                    consultation.Question.ThreadRootId,
                    memberId,
                    "The supporting evidence is on page three.",
                    now.AddMinutes(4)), cancellationToken);
                await Assert.That(participantReply.Failure).IsNull();
                await Assert.That(participantReply.Question!.Status)
                    .IsEqualTo(CompetitionQuestionStatus.Pending);
                await Assert.That(outbox.Published
                        .OfType<DeliverCompetitionQuestionNotification>()
                        .Any(message => message.ThreadRootId == consultation.Question.ThreadRootId
                            && message.RecipientUserIds.Contains(memberId)))
                    .IsTrue();
            }
            finally
            {
                var resolvedRoot = Path.GetFullPath(storageRoot);
                var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
                if (resolvedRoot.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(resolvedRoot))
                {
                    Directory.Delete(resolvedRoot, recursive: true);
                }
            }
        });
    }

    private static User User(Guid id, string name, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        PasswordHash = "test",
        Kind = UserKind.Human,
        AccountStatus = UserAccountStatus.Active,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static MemoryStream Pdf(string body) => new(PdfBytes(body));

    private static byte[] PdfBytes(string body) =>
        System.Text.Encoding.ASCII.GetBytes($"%PDF-1.7\n{body}\n%%EOF\n");

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
