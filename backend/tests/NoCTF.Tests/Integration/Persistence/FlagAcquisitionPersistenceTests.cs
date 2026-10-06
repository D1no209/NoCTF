using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.GameplayFacts.CheatIncidents;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Challenges.Attachments;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Infrastructure.GameplayFacts.Management;
using NoCTF.Infrastructure.GameplayFacts.CheatIncidents;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Persistence.PostgreSql;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class FlagAcquisitionPersistenceTests
{
    [Test, Timeout(300_000)]
    public Task Admission_freezes_team_access_and_preserves_legacy_compatibility(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase("acquisition").WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString(), setup => setup.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName))
            .UseSnakeCaseNamingConvention().Options;
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var competitionId = Guid.NewGuid(); var templateId = Guid.NewGuid(); var ccId = Guid.NewGuid();
        var teamId = Guid.NewGuid(); var otherTeamId = Guid.NewGuid(); var attachmentId = Guid.NewGuid(); var fileId = Guid.NewGuid();
        foreach (var id in users) db.Users.Add(new User { Id = id, UserName = id.ToString("N"), NormalizedUserName = id.ToString("N").ToUpperInvariant(), Email = $"{id:N}@test.invalid",
            PasswordHash = "test", AccountStatus = UserAccountStatus.Active, CreatedAt = now, UpdatedAt = now });
        db.Competitions.Add(new CtfCompetition { Id = competitionId, OwnerId = users[0], Title = "Acquisition", Status = CompetitionStatus.Running,
            StartAt = now.AddHours(-1), EndAt = now.AddHours(1), ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
            FlagDerivationSecret = new byte[32], CreatedAt = now, UpdatedAt = now });
        db.Challenges.Add(new CtfChallenge { Id = templateId, OwnerId = users[0], Title = "Static", Direction = "Web", CreatedAt = now, UpdatedAt = now,
            Definition = new CtfChallengeDefinition { Runtime = new ContainerChallengeRuntimeTemplate
            { Allocation = PersistedRuntimeAllocation.PerTeam, FlagSource = PersistedRuntimeFlagSource.Static,
                Services = [new() { Name = "app", Image = "test.invalid/static:1" }] } } });
        db.CompetitionChallenges.Add(new CtfCompetitionChallenge { Id = ccId, CompetitionId = competitionId, ChallengeId = templateId,
            Rules = TestConfigurations.Rules(GameMode.Ctf), IsPublished = true, UpdatedAt = now });
        db.Teams.Add(new Team { Id = teamId, CompetitionId = competitionId, Name = "Team", CaptainId = users[0], MemberIds = [users[0], users[1]],
            InvitationToken = new string('a',32), RegistrationStatus = TeamRegistrationStatus.Approved });
        db.Teams.Add(new Team { Id = otherTeamId, CompetitionId = competitionId, Name = "Other", CaptainId = users[2], MemberIds = [users[2]],
            InvitationToken = new string('b',32), RegistrationStatus = TeamRegistrationStatus.Approved });
        db.Files.Add(new StoredFile { Id = fileId, ObjectKey = "acquisition/file", FileName = "file.txt", ContentType = "text/plain",
            ByteLength = 1, Sha256 = new byte[32], CreatedAt = now });
        db.Set<ChallengeAttachment>().Add(new() { Id = attachmentId, ChallengeId = templateId, FileId = fileId, CreatedAt = now });
        await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
        var publisher = Substitute.For<IPostCommitMessagePublisher>();
        async Task<GameplayFact> Submit(Guid userId, DateTimeOffset at)
        {
            var intake = new GameplayFactIntakeStore(db, publisher);
            var snapshot = (await intake.LoadAdmissionAsync(competitionId, ccId, userId, ct))!;
            var id = Guid.NewGuid();
            var accepted = await intake.TryAcceptFlagAsync(new(id, competitionId, snapshot.TeamId, ccId, userId,
                GameplayFactKind.FlagAttempt, "flag{test}", NoCTF.Application.Challenges.Flags.ManageChallengeFlags.Hash("flag{test}"), at), snapshot, null, ct);
            await Assert.That(accepted.State).IsEqualTo(GameplayFactAcceptanceState.Created);
            db.ChangeTracker.Clear();
            return await db.GameplayFacts.SingleAsync(fact => fact.Id == id, ct);
        }
        var first = await Submit(users[0], now);
        await Assert.That(first.AcquisitionEvidence!.MissingEvidence).IsEqualTo(GameplayFactFailureCode.StaticFlagWithoutContainerAndAttachment);
        first.State = GameplayFactState.Completed;
        first.Result = GameplayFactResult.Rejected;
        first.FailureCode = GameplayFactFailureCode.StaticFlagWithoutContainerAndAttachment;
        await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
        var incidents = new CheatIncidentStore(db, publisher, Substitute.For<ICompetitionEventRecorder>());
        var listed = await incidents.ListAsync(new(competitionId, null, null, null, null, null,
            now.AddHours(-1), now.AddHours(1), null, null, 10), ct);
        await Assert.That(listed!.PendingCount).IsEqualTo(1);
        await Assert.That(listed.Items.Single().OwnerTeamId).IsNull();
        var detail = await incidents.GetDetailAsync(new(competitionId, first.Id, users[0], false, now), ct);
        await Assert.That(detail!.AcquisitionEvidence!.MissingEvidence)
            .IsEqualTo(GameplayFactFailureCode.StaticFlagWithoutContainerAndAttachment);
        db.ChangeTracker.Clear();
        // Obtaining resources after this admission must not change the earlier snapshot.
        await new ChallengeAttachmentStore(db).RecordPlayerDownloadAsync(competitionId, ccId, teamId, users[1], attachmentId, ct);
        db.RuntimeInstances.Add(new PlayerRuntimeInstance { Id = Guid.NewGuid(), CompetitionId = competitionId, CompetitionChallengeId = ccId,
            TeamId = teamId, RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker, State = RuntimeState.Stopped,
            CreatedAt = now, RunningAt = now.AddSeconds(1), StoppedAt = now.AddSeconds(2) });
        await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
        await Assert.That((await db.GameplayFacts.SingleAsync(fact => fact.Id == first.Id, ct)).AcquisitionEvidence!.MissingEvidence)
            .IsEqualTo(GameplayFactFailureCode.StaticFlagWithoutContainerAndAttachment);
        db.ChangeTracker.Clear();
        var shared = await Submit(users[0], now.AddMinutes(1));
        await Assert.That(shared.AcquisitionEvidence!.MissingEvidence).IsNull();
        await Assert.That(shared.AcquisitionEvidence.RuntimeInstanceId).IsNotNull();
        await Assert.That(shared.AcquisitionEvidence.AttachmentDownloadFactId).IsNotNull();
        db.ChangeTracker.Clear();
        var other = await Submit(users[2], now.AddMinutes(1));
        await Assert.That(other.AcquisitionEvidence!.MissingEvidence).IsEqualTo(GameplayFactFailureCode.StaticFlagWithoutContainerAndAttachment);
        db.ChangeTracker.Clear();
        var legacy = GameplayFactGeneratedCatalog.Create(GameplayFactKind.FlagAttempt);
        legacy.Id = Guid.NewGuid(); legacy.CompetitionId = competitionId; legacy.CompetitionChallengeId = ccId; legacy.TeamId = otherTeamId;
        legacy.ActorUserId = users[2]; legacy.Value = "wrong"; legacy.Result = GameplayFactResult.Wrong;
        legacy.State = GameplayFactState.Completed; legacy.OccurredAt = legacy.UpdatedAt = now.AddMinutes(-1);
        db.GameplayFacts.Add(legacy); await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
        var compatible = await Submit(users[2], now.AddMinutes(2));
        await Assert.That(compatible.AcquisitionEvidence!.Source).IsEqualTo(FlagAcquisitionEvidenceSource.LegacySubmission);
        await Assert.That(compatible.AcquisitionEvidence.MissingEvidence).IsNull();
        await Assert.That(compatible.AcquisitionEvidence.RuntimeInstanceId).IsNull();
        db.ChangeTracker.Clear();
        var management = new GameplayFactManagementStore(db, publisher);
        var page = await management.ListAdminPageAsync(new(competitionId, null, null, null, null, null, null, null, null, null, null, null, null, null), 0, 100, true, ct);
        await Assert.That(page.Items.Any(item => item.Kind == GameplayFactKind.AttachmentDownload)).IsFalse();
        await Assert.That((await db.GameplayFacts.SingleAsync(fact => fact.Id == legacy.Id, ct)).AcquisitionEvidence).IsNull();
    });
}
