using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Membership;
using NoCTF.Infrastructure.Teams.Registration;
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class TeamMutationConcurrencyPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Competition_team_mutations_are_serialized_per_competition(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_team_mutation_concurrency")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using (var migrationDb = new NoCtfDbContext(options))
                await migrationDb.Database.EnsureCreatedAsync(cancellationToken);

            await SameUserCreatesTwiceAsync(options, cancellationToken);
            await CreateWhileJoiningAsync(options, cancellationToken);
            await DifferentUsersJoinAsync(options, cancellationToken);
            await JoinWhileRunningFollowsRegistrationSettingAsync(options, cancellationToken);
            await InvitationReadAuthorizationAsync(options, cancellationToken);
            await BannedTeamRejectsOrganizationMutationsAsync(options, cancellationToken);
        });
    }

    private static async Task SameUserCreatesTwiceAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(10);
        var owner = User(Guid.CreateVersion7(now), "double-owner", now);
        var participant = User(Guid.CreateVersion7(now.AddTicks(1)), "double-player", now);
        var competition = Competition(
            Guid.CreateVersion7(now.AddTicks(2)),
            owner.Id,
            "Double registration",
            now);
        await SeedAsync(options, [owner, participant], [competition], [], ct);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = CreateAsync(
            options,
            new(competition.Id, participant.Id, "Double A", now.AddMinutes(1), "default"),
            start.Task,
            ct);
        var second = CreateAsync(
            options,
            new(competition.Id, participant.Id, "Double B", now.AddMinutes(1).AddTicks(1), "default"),
            start.Task,
            ct);
        start.SetResult();
        var results = await Task.WhenAll(first, second);

        await Assert.That(results.Count(result => result.Team is not null)).IsEqualTo(1);
        await Assert.That(results.Count(result =>
            result.Failure == TeamRegistrationFailure.UserAlreadyRegistered)).IsEqualTo(1);
        await using var verification = new NoCtfDbContext(options);
        await Assert.That(await verification.Teams.CountAsync(
            team => team.CompetitionId == competition.Id
                && team.MemberIds.Contains(participant.Id),
            ct)).IsEqualTo(1);
    }

    private static async Task CreateWhileJoiningAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(20);
        var owner = User(Guid.CreateVersion7(now), "join-owner", now);
        var captain = User(Guid.CreateVersion7(now.AddTicks(1)), "join-captain", now);
        var participant = User(Guid.CreateVersion7(now.AddTicks(2)), "join-player", now);
        var competition = Competition(
            Guid.CreateVersion7(now.AddTicks(3)),
            owner.Id,
            "Create versus join",
            now);
        const string invitationToken = "0123456789ABCDEFGHIJKLMNOPQRSTUV";
        var existingTeam = Team(
            Guid.CreateVersion7(now.AddTicks(4)),
            competition.Id,
            captain.Id,
            "Existing Team",
            invitationToken,
            now);
        await SeedAsync(
            options,
            [owner, captain, participant],
            [competition],
            [existingTeam],
            ct);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var createTask = CreateAsync(
            options,
            new(competition.Id, participant.Id, "Created Team", now.AddMinutes(1), "default"),
            start.Task,
            ct);
        var joinTask = JoinAsync(
            options,
            competition.Id,
            invitationToken,
            participant.Id,
            now.AddMinutes(1),
            start.Task,
            ct);
        start.SetResult();
        await Task.WhenAll(createTask, joinTask);
        var created = await createTask;
        var joined = await joinTask;

        await Assert.That((created.Team is not null ? 1 : 0) + (joined is null ? 1 : 0))
            .IsEqualTo(1);
        if (created.Team is not null)
            await Assert.That(joined).IsEqualTo(TeamMembershipFailure.UserAlreadyRegistered);
        else
            await Assert.That(created.Failure).IsEqualTo(TeamRegistrationFailure.UserAlreadyRegistered);

        await using var verification = new NoCtfDbContext(options);
        var teams = await verification.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competition.Id)
            .ToArrayAsync(ct);
        await Assert.That(teams.Count(team => team.MemberIds.Contains(participant.Id)))
            .IsEqualTo(1);
    }

    private static async Task DifferentUsersJoinAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(30);
        var owner = User(Guid.CreateVersion7(now), "parallel-owner", now);
        var captain = User(Guid.CreateVersion7(now.AddTicks(1)), "parallel-captain", now);
        var firstParticipant = User(Guid.CreateVersion7(now.AddTicks(2)), "parallel-first", now);
        var secondParticipant = User(Guid.CreateVersion7(now.AddTicks(3)), "parallel-second", now);
        var competition = Competition(
            Guid.CreateVersion7(now.AddTicks(4)),
            owner.Id,
            "Parallel membership",
            now);
        const string invitationToken = "ZYXWVUTSRQPONMLKJIHGFEDCBA987654";
        var existingTeam = Team(
            Guid.CreateVersion7(now.AddTicks(5)),
            competition.Id,
            captain.Id,
            "Parallel Team",
            invitationToken,
            now);
        await SeedAsync(
            options,
            [owner, captain, firstParticipant, secondParticipant],
            [competition],
            [existingTeam],
            ct);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = JoinAsync(
            options,
            competition.Id,
            invitationToken,
            firstParticipant.Id,
            now.AddMinutes(1),
            start.Task,
            ct);
        var second = JoinAsync(
            options,
            competition.Id,
            invitationToken,
            secondParticipant.Id,
            now.AddMinutes(1).AddTicks(1),
            start.Task,
            ct);
        start.SetResult();
        var results = await Task.WhenAll(first, second);

        await Assert.That(results.All(result => result is null)).IsTrue();
        await using var verification = new NoCtfDbContext(options);
        var memberIds = await verification.Teams.AsNoTracking()
            .Where(team => team.Id == existingTeam.Id)
            .Select(team => team.MemberIds)
            .SingleAsync(ct);
        await Assert.That(memberIds).Contains(captain.Id);
        await Assert.That(memberIds).Contains(firstParticipant.Id);
        await Assert.That(memberIds).Contains(secondParticipant.Id);
        await Assert.That(memberIds.Length).IsEqualTo(3);
    }

    private static async Task InvitationReadAuthorizationAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(35);
        var owner = User(Guid.CreateVersion7(now), "invitation-owner", now);
        var captain = User(Guid.CreateVersion7(now.AddTicks(1)), "invitation-captain", now);
        var outsider = User(Guid.CreateVersion7(now.AddTicks(2)), "invitation-outsider", now);
        var competition = Competition(
            Guid.CreateVersion7(now.AddTicks(3)), owner.Id, "Invitation read", now);
        const string invitationToken = "READREADREADREADREADREADREADREAD";
        var team = Team(
            Guid.CreateVersion7(now.AddTicks(4)),
            competition.Id,
            captain.Id,
            "Readable",
            invitationToken,
            now);
        await SeedAsync(options, [owner, captain, outsider], [competition], [team], ct);

        await using var db = new NoCtfDbContext(options);
        var membership = new TeamMembershipStore(db, new NoopOutbox());
        var captainRead = await membership.GetInvitationAsync(
            competition.Id, team.Id, captain.Id, ct);
        var ownerRead = await membership.GetInvitationAsync(
            competition.Id, team.Id, owner.Id, ct);
        var outsiderRead = await membership.GetInvitationAsync(
            competition.Id, team.Id, outsider.Id, ct);

        await Assert.That(captainRead.Token).IsEqualTo(invitationToken);
        await Assert.That(captainRead.Failure).IsNull();
        await Assert.That(ownerRead.Token).IsEqualTo(invitationToken);
        await Assert.That(ownerRead.Failure).IsNull();
        await Assert.That(outsiderRead.Token).IsNull();
        await Assert.That(outsiderRead.Failure)
            .IsEqualTo(TeamMembershipFailure.TeamForbidden);
    }

    private static async Task JoinWhileRunningFollowsRegistrationSettingAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(35);
        var owner = User(Guid.CreateVersion7(now), "running-owner", now);
        var allowedCaptain = User(Guid.CreateVersion7(now.AddTicks(1)), "running-allowed-captain", now);
        var allowedParticipant = User(Guid.CreateVersion7(now.AddTicks(2)), "running-allowed-player", now);
        var blockedCaptain = User(Guid.CreateVersion7(now.AddTicks(3)), "running-blocked-captain", now);
        var blockedParticipant = User(Guid.CreateVersion7(now.AddTicks(4)), "running-blocked-player", now);
        var allowedCompetition = Competition(
            Guid.CreateVersion7(now.AddTicks(5)), owner.Id, "Running registration allowed", now);
        allowedCompetition.Status = CompetitionStatus.Running;
        allowedCompetition.AllowTeamRegistrationWhileRunning = true;
        var blockedCompetition = Competition(
            Guid.CreateVersion7(now.AddTicks(6)), owner.Id, "Running registration blocked", now);
        blockedCompetition.Status = CompetitionStatus.Running;
        blockedCompetition.AllowTeamRegistrationWhileRunning = false;
        const string allowedToken = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string blockedToken = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        var allowedTeam = Team(
            Guid.CreateVersion7(now.AddTicks(7)),
            allowedCompetition.Id,
            allowedCaptain.Id,
            "Allowed Team",
            allowedToken,
            now);
        var blockedTeam = Team(
            Guid.CreateVersion7(now.AddTicks(8)),
            blockedCompetition.Id,
            blockedCaptain.Id,
            "Blocked Team",
            blockedToken,
            now);
        await SeedAsync(
            options,
            [owner, allowedCaptain, allowedParticipant, blockedCaptain, blockedParticipant],
            [allowedCompetition, blockedCompetition],
            [allowedTeam, blockedTeam],
            ct);

        await using var db = new NoCtfDbContext(options);
        var membership = new TeamMembershipStore(db, new NoopOutbox());
        var allowed = await membership.JoinByInvitationAsync(
            allowedCompetition.Id,
            allowedToken,
            allowedParticipant.Id,
            now,
            ct);
        var blocked = await membership.JoinByInvitationAsync(
            blockedCompetition.Id,
            blockedToken,
            blockedParticipant.Id,
            now,
            ct);

        await Assert.That(allowed).IsNull();
        await Assert.That(blocked).IsEqualTo(TeamMembershipFailure.MembershipLocked);
        var allowedMembers = await db.Teams.AsNoTracking()
            .Where(team => team.Id == allowedTeam.Id)
            .Select(team => team.MemberIds)
            .SingleAsync(ct);
        await Assert.That(allowedMembers).Contains(allowedParticipant.Id);
    }

    private static async Task BannedTeamRejectsOrganizationMutationsAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(40);
        var owner = User(Guid.CreateVersion7(now), "banned-owner", now);
        var captain = User(Guid.CreateVersion7(now.AddTicks(1)), "banned-captain", now);
        var member = User(Guid.CreateVersion7(now.AddTicks(2)), "banned-member", now);
        var outsider = User(Guid.CreateVersion7(now.AddTicks(3)), "banned-outsider", now);
        var competition = Competition(
            Guid.CreateVersion7(now.AddTicks(4)), owner.Id, "Banned team", now);
        const string invitationToken = "BANDBANDBANDBANDBANDBANDBANDBAND";
        var team = Team(
            Guid.CreateVersion7(now.AddTicks(5)),
            competition.Id,
            captain.Id,
            "Frozen",
            invitationToken,
            now);
        team.MemberIds = [captain.Id, member.Id];
        team.IsBanned = true;
        await SeedAsync(options, [owner, captain, member, outsider], [competition], [team], ct);

        await using var db = new NoCtfDbContext(options);
        var membership = new TeamMembershipStore(db, new NoopOutbox());
        var registration = new TeamRegistrationStore(db, new NoopOutbox());
        await Assert.That(await membership.JoinByInvitationAsync(
            competition.Id, invitationToken, outsider.Id, now, ct))
            .IsEqualTo(TeamMembershipFailure.TeamBanned);
        await Assert.That((await membership.RotateInvitationAsync(
            competition.Id, team.Id, captain.Id,
            "FROZFROZFROZFROZFROZFROZFROZFROZ", ct)).Failure)
            .IsEqualTo(TeamMembershipFailure.TeamBanned);
        await Assert.That((await membership.GetInvitationAsync(
            competition.Id, team.Id, captain.Id, ct)).Failure)
            .IsEqualTo(TeamMembershipFailure.TeamBanned);
        await Assert.That(await membership.RemoveMemberAsync(
            competition.Id, team.Id, member.Id, captain.Id, ct))
            .IsEqualTo(TeamMembershipFailure.TeamBanned);
        await Assert.That(await membership.LeaveAsync(competition.Id, member.Id, ct))
            .IsEqualTo(TeamMembershipFailure.TeamBanned);
        await Assert.That(await membership.TransferCaptainAsync(
            competition.Id, team.Id, captain.Id, member.Id, ct))
            .IsEqualTo(TeamMembershipFailure.TeamBanned);
        await Assert.That((await registration.UpdateAsync(new(
            competition.Id, team.Id, "Renamed"), ct)).Failure)
            .IsEqualTo(TeamRegistrationFailure.TeamBanned);
        await Assert.That(await registration.SoftDeleteAsync(
            competition.Id, team.Id, captain.Id, now, ct))
            .IsEqualTo(TeamRegistrationFailure.TeamBanned);
    }

    private static async Task<TeamCreateStoreResult> CreateAsync(
        DbContextOptions<NoCtfDbContext> options,
        CreateTeamCommand command,
        Task start,
        CancellationToken ct)
    {
        await start.WaitAsync(ct);
        await using var db = new NoCtfDbContext(options);
        return await new TeamRegistrationStore(db, new NoopOutbox()).TryCreateAsync(
            command,
            TeamRegistrationStatus.Approved,
            ct);
    }

    private static async Task<TeamMembershipFailure?> JoinAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid competitionId,
        string invitationToken,
        Guid userId,
        DateTimeOffset now,
        Task start,
        CancellationToken ct)
    {
        await start.WaitAsync(ct);
        await using var db = new NoCtfDbContext(options);
        return await new TeamMembershipStore(db, new NoopOutbox()).JoinByInvitationAsync(
            competitionId,
            invitationToken,
            userId,
            now,
            ct);
    }

    private static async Task SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        IReadOnlyCollection<User> users,
        IReadOnlyCollection<Competition> competitions,
        IReadOnlyCollection<Team> teams,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        db.Users.AddRange(users);
        db.Competitions.AddRange(competitions);
        db.Teams.AddRange(teams);
        await db.SaveChangesAsync(ct);
    }

    private static User User(Guid id, string name, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        PasswordHash = "unused",
        Kind = UserKind.Human,
        Role = UserRole.User,
        AccountStatus = UserAccountStatus.Active,
        EmailVerifiedAt = now,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        string title,
        DateTimeOffset now) => new()
    {
        Id = id,
        OwnerId = ownerId,
        Title = title,
        Mode = GameMode.Ctf,
        Status = CompetitionStatus.Visible,
        ConfigurationJson = "{}",
        MaxTeamMembers = 5,
        FlagDerivationSecret = new byte[32],
        StartAt = now.AddHours(1),
        EndAt = now.AddHours(2),
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static Team Team(
        Guid id,
        Guid competitionId,
        Guid captainId,
        string name,
        string invitationToken,
        DateTimeOffset now) => new()
    {
        Id = id,
        CompetitionId = competitionId,
        Name = name,
        CaptainId = captainId,
        MemberIds = [captainId],
        InvitationToken = invitationToken,
        RegistrationStatus = TeamRegistrationStatus.Approved,
        RegisteredAt = now
    };

    private sealed class BlockingEventRecorder(ICompetitionEventRecorder inner)
        : ICompetitionEventRecorder
    {
        private readonly TaskCompletionSource entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => entered.Task;

        public void Release() => release.TrySetResult();

        public async ValueTask<Guid> RecordAsync(
            CompetitionEventDraft draft,
            CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await inner.RecordAsync(draft, cancellationToken);
        }
    }

    private sealed class NoopOutbox : ITransactionalMessageOutbox
    {
        public int FlushCount { get; private set; }

        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync()
        {
            FlushCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyLeaderboardCache : ILeaderboardCache
    {
        public Task<LeaderboardResponse?> GetAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<LeaderboardResponse?>(null);

        public Task RefreshAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task InvalidateAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
