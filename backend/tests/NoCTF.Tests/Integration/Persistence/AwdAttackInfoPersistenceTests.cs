using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class AwdRoundCoordinationTests
{
    [Test, Timeout(300_000)]
    public Task Participant_attack_info_filters_windows_and_returns_only_public_metadata(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
        var fixture = await SeedAsync(options, ct);
        await using var db = new NoCtfDbContext(options);
        var competition = await db.Competitions.SingleAsync(item => item.Id == fixture.CompetitionId, ct);
        var configuration = (AwdCompetitionModeConfiguration)competition.ModeConfiguration!;
        configuration.AwdOptions = configuration.AwdOptions with { FlagsPerStore = 8 };
        await db.SaveChangesAsync(ct);
        var outbox = new RecordingOutbox();
        var coordinator = new AwdRoundCoordinator(db, new AwdRoundConfigurationCatalog(), outbox, new MutableTimeProvider(fixture.Now), Secrets());
        await coordinator.AdvanceAsync(new(fixture.CompetitionId, fixture.Now), ct);
        await coordinator.GenerateFlagsAsync(outbox.Published.OfType<GenerateAwdFlags>().Single(), ct);
        var issues = await db.AwdFlagIssues.OrderBy(item => item.Ordinal).ToArrayAsync(ct);
        await Assert.That(issues.Length).IsEqualTo(8);
        foreach (var issue in issues)
        {
            issue.State = AwdFlagIssueState.Issued;
            issue.PublicLocator = "user/public-target";
            issue.ProtectedReadContext = "PRIVATE-CHECKER-CONTEXT";
        }
        issues[0].ActiveExpiryTicks = issues[0].ActiveStartTicks + TimeSpan.FromSeconds(10).Ticks;
        issues[2].ActiveExpiryTicks = issues[2].ActiveStartTicks;
        issues[3].ExpiryRound = 1;
        issues[4].State = AwdFlagIssueState.Revoked;
        issues[5].PublicLocator = null;
        issues[6].IssuedAt = fixture.Now.AddHours(1);
        var deletedFlag = await db.ChallengeFlags.SingleAsync(item => item.Id == issues[7].FlagId, ct);
        deletedFlag.DeletedAt = fixture.Now;
        await db.SaveChangesAsync(ct);
        var reader = new AwdAttackInfoReader(db);
        var list = new ListAwdAttackInfo(new CompetitionChallengeReadAccess(db), reader);
        Task<AwdAttackInfoPage?> Read(DateTimeOffset now, int offset = 0) => list.ExecuteAsync(fixture.OwnerId,
            fixture.CompetitionId, fixture.CompetitionChallengeId, fixture.TeamId, now, offset, 1, ct);
        var first = (await Read(fixture.Now))!;
        var second = (await Read(fixture.Now, 1))!;
        await Assert.That(first.Total).IsEqualTo(2);
        await Assert.That(first.Items.Count).IsEqualTo(1);
        await Assert.That(second.Items.Single().FlagId).IsNotEqualTo(first.Items.Single().FlagId);
        await Assert.That(first.Items.Single().PublicLocator).IsEqualTo("user/public-target");
        await Assert.That(first.Items.Single().RemainingActiveSeconds).IsEqualTo(10d);
        var json = JsonSerializer.Serialize(first);
        await Assert.That(json.Contains("PRIVATE-CHECKER-CONTEXT", StringComparison.Ordinal)).IsFalse();
        await Assert.That(json.Contains("ProtectedReadContext", StringComparison.Ordinal)).IsFalse();
        var secretFlag = await db.ChallengeFlags.Where(item => item.Id == issues[0].FlagId).Select(item => item.Flag).SingleAsync(ct);
        await Assert.That(json.Contains(secretFlag, StringComparison.Ordinal)).IsFalse();
        await Assert.That((await Read(fixture.Now.AddSeconds(10)))!.Total).IsEqualTo(1);
        await Assert.That((await Read(fixture.Now.AddDays(1)))!.Total).IsEqualTo(0);
        await Assert.That(await list.ExecuteAsync(Guid.NewGuid(), fixture.CompetitionId, fixture.CompetitionChallengeId,
            fixture.TeamId, fixture.Now, 0, 10, ct)).IsNull();
        await Assert.That(await reader.ReadAsync(fixture.CompetitionId, fixture.CompetitionChallengeId, fixture.TeamId,
            Guid.NewGuid(), fixture.Now, 0, 10, ct)).IsNull();
        var playerId = Guid.CreateVersion7();
        var playerTeamId = Guid.CreateVersion7();
        db.Users.Add(new User { Id = playerId, UserName = "attack-reader", NormalizedUserName = "ATTACK-READER",
            Email = "attack-reader@example.test", PasswordHash = "test", CreatedAt = fixture.Now, UpdatedAt = fixture.Now });
        db.Teams.Add(new Team { Id = playerTeamId, CompetitionId = fixture.CompetitionId, Name = "Reader",
            CaptainId = playerId, MemberIds = [playerId], InvitationToken = new string('a', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = fixture.Now });
        await db.SaveChangesAsync(ct);
        Task<AwdAttackInfoPage?> Opponent(DateTimeOffset at) => list.ExecuteAsync(playerId, fixture.CompetitionId,
            fixture.CompetitionChallengeId, fixture.TeamId, at, 0, 10, ct);
        await Assert.That(await Opponent(fixture.Now.AddSeconds(-1))).IsNull();
        await Assert.That((await Opponent(fixture.Now))!.Total).IsEqualTo(2);
        var accessMode = competition.AccessMode;
        competition.AccessMode = CompetitionAccessMode.StaffOnly;
        await db.SaveChangesAsync(ct);
        await Assert.That(await Opponent(fixture.Now)).IsNull();
        competition.AccessMode = accessMode;
        competition.Status = CompetitionStatus.Paused;
        db.CompetitionEvents.Add(new CompetitionLifecycleChangedEvent { Id = Guid.CreateVersion7(),
            CompetitionId = fixture.CompetitionId, Level = CompetitionEventLevel.Information,
            Visibility = CompetitionEventVisibility.Public, SubjectType = NoCTF.Domain.Shared.EntityReferenceKind.Competition,
            SubjectId = fixture.CompetitionId, PreviousCompetitionStatus = CompetitionStatus.Running,
            CompetitionStatus = CompetitionStatus.Paused, OccurredAt = fixture.Now.AddSeconds(5) });
        await db.SaveChangesAsync(ct);
        var paused = (await Read(fixture.Now.AddSeconds(50)))!;
        await Assert.That(paused.Total).IsEqualTo(2);
        await Assert.That(paused.Items.Single().RemainingActiveSeconds).IsEqualTo(5d);
        var challenge = await db.CompetitionChallenges.SingleAsync(item => item.Id == fixture.CompetitionChallengeId, ct);
        challenge.IsPublished = false;
        await db.SaveChangesAsync(ct);
        await Assert.That(await Read(fixture.Now)).IsNull();
        challenge.IsPublished = true;
        var team = await db.Teams.SingleAsync(item => item.Id == fixture.TeamId, ct);
        team.IsBanned = true;
        await db.SaveChangesAsync(ct);
        await Assert.That(await Read(fixture.Now)).IsNull();
    });
}
