using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
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
                await migrationDb.Database.MigrateAsync(cancellationToken);

            await RefreshWhileCreatingAsync(options, cancellationToken);
            await SameUserCreatesTwiceAsync(options, cancellationToken);
            await CreateWhileJoiningAsync(options, cancellationToken);
            await DifferentUsersJoinAsync(options, cancellationToken);
        });
    }

    private static async Task RefreshWhileCreatingAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var owner = User(Guid.CreateVersion7(now), "refresh-owner", now);
        var participant = User(Guid.CreateVersion7(now.AddTicks(1)), "refresh-player", now);
        var competition = Competition(
            Guid.CreateVersion7(now.AddTicks(2)),
            owner.Id,
            "Refresh versus registration",
            leaderboardDirty: true,
            now);
        await SeedAsync(options, [owner, participant], [competition], [], ct);

        await using var registrationDb = new NoCtfDbContext(options);
        await using var refreshDb = new NoCtfDbContext(options);
        var registrationOutbox = new NoopOutbox();
        var eventStore = new CompetitionEventStore(registrationDb, registrationOutbox);
        var blocker = new BlockingEventRecorder(eventStore);
        var store = new TeamRegistrationStore(
            registrationDb,
            registrationOutbox,
            eventRecorder: blocker);
        var registrationTask = store.TryCreateAsync(
            new(competition.Id, participant.Id, "Refresh Team", now.AddMinutes(1), "default"),
            TeamRegistrationStatus.Approved,
            ct);

        await blocker.Entered.WaitAsync(TimeSpan.FromSeconds(30), ct);
        try
        {
            await BackendMessageHandlers.Handle(
                    new RefreshDirtyLeaderboards(now),
                    refreshDb,
                    new NoopOutbox(),
                    ct)
                .WaitAsync(TimeSpan.FromSeconds(30), ct);
        }
        finally
        {
            blocker.Release();
        }

        var result = await registrationTask.WaitAsync(TimeSpan.FromSeconds(30), ct);
        await Assert.That(result.Team).IsNotNull();
        await Assert.That(result.Failure).IsNull();
        await Assert.That(registrationOutbox.FlushCount).IsEqualTo(1);

        await using var verification = new NoCtfDbContext(options);
        await Assert.That(await verification.Teams.CountAsync(
            team => team.CompetitionId == competition.Id,
            ct)).IsEqualTo(1);
        await Assert.That(await verification.CompetitionEvents.CountAsync(
            item => item.CompetitionId == competition.Id
                && item.Kind == CompetitionEventKind.TeamRegistered,
            ct)).IsEqualTo(1);
        await Assert.That(await verification.Competitions
            .Where(item => item.Id == competition.Id)
            .Select(item => item.LeaderboardDirty)
            .SingleAsync(ct)).IsTrue();
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
            leaderboardDirty: false,
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
            leaderboardDirty: false,
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
            leaderboardDirty: false,
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
        await Assert.That(await verification.Competitions
            .Where(item => item.Id == competition.Id)
            .Select(item => item.LeaderboardDirty)
            .SingleAsync(ct)).IsTrue();
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
        NormalizedEmail = $"{name}@example.test".ToUpperInvariant(),
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
        bool leaderboardDirty,
        DateTimeOffset now) => new()
    {
        Id = id,
        OwnerId = ownerId,
        Title = title,
        Mode = GameMode.Ctf,
        Status = CompetitionStatus.Visible,
        LeaderboardDirty = leaderboardDirty,
        MaxTeamMembers = 5,
        FlagDerivationSecret = new byte[32],
        StartAt = now.AddHours(1),
        EndAt = now.AddHours(2),
        CreatedAt = now,
        UpdatedAt = now,
        ConfigurationUpdatedAt = now
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
        NormalizedName = name.ToUpperInvariant(),
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
        public ValueTask PublishToRunnerPoolAsync<T>(T message)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;
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
}
