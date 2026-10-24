using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Flags;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Flags;
using NoCTF.Infrastructure.Teams.Registration;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class RuntimeFlagQueryPersistenceTests
{
    [Test, Arguments(GameMode.Ctf), Arguments(GameMode.Awd), Arguments(GameMode.Awdp)]
    [Timeout(300_000)]
    public async Task Runtime_flags_are_isolated_by_mode_team_instance_and_lifetime(GameMode mode, CancellationToken ct)
    {
        await Run(mode, async (db, fixture) =>
        {
            var current = Flag(fixture, fixture.TeamId, fixture.RuntimeId, fixture.Now.AddMinutes(-10), mode);
            db.ChallengeFlags.Add(current);
            var foreign = Flag(fixture, fixture.OtherTeamId, Guid.NewGuid(), fixture.Now.AddMinutes(-5), mode);
            db.ChallengeFlags.Add(foreign);
            var unrelatedInstance = Flag(fixture, fixture.TeamId, Guid.NewGuid(), fixture.Now.AddMinutes(-5), GameMode.Awdp);
            db.ChallengeFlags.Add(unrelatedInstance);
            await db.SaveChangesAsync(ct);
            var reader = new RuntimeFlagReader(db);
            var page = await reader.ReadAsync(new(fixture.RuntimeId, false, 0, 10, fixture.Now), ct);
            await Assert.That(page.Total).IsEqualTo(1);
            await Assert.That(page.Items.Single().Flag.Id).IsEqualTo(current.Id);
            await Assert.That(page.Items.Single().Source).IsEqualTo(mode switch
            {
                GameMode.Ctf => NoCTF.Application.Runtime.Flags.RuntimeFlagSource.Team,
                GameMode.Awd => NoCTF.Application.Runtime.Flags.RuntimeFlagSource.AwdRound,
                _ => NoCTF.Application.Runtime.Flags.RuntimeFlagSource.Instance
            });
            if (mode == GameMode.Awdp)
            {
                current.ValidStart = fixture.Now.AddMinutes(1);
                await db.SaveChangesAsync(ct);
                await Assert.That((await reader.ReadAsync(new(fixture.RuntimeId, false, 0, 10, fixture.Now), ct)).Total).IsEqualTo(0);
                await Assert.That((await reader.ReadAsync(new(fixture.RuntimeId, true, 0, 10, fixture.Now), ct)).Items.Single().State)
                    .IsEqualTo(RuntimeFlagState.Scheduled);
                current.ValidStart = null;
            }
            current.ValidUntil = fixture.Now;
            await db.SaveChangesAsync(ct);
            await Assert.That((await reader.ReadAsync(new(fixture.RuntimeId, false, 0, 10, fixture.Now), ct)).Total).IsEqualTo(0);
            page = await reader.ReadAsync(new(fixture.RuntimeId, true, 0, 10, fixture.Now), ct);
            await Assert.That(page.Items.Single().State).IsEqualTo(RuntimeFlagState.Expired);
            current.DeletedAt = fixture.Now;
            await db.SaveChangesAsync(ct);
            page = await reader.ReadAsync(new(fixture.RuntimeId, true, 0, 10, fixture.Now), ct);
            await Assert.That(page.Items.Single().State).IsEqualTo(RuntimeFlagState.Deleted);
        }, ct);
    }

    [Test, Timeout(300_000)]
    public async Task Awd_history_only_contains_overlapping_rounds_and_pages_have_a_stable_total(CancellationToken ct)
    {
        await Run(GameMode.Awd, async (db, fixture) =>
        {
            for (var round = 1; round <= 5; round++)
            {
                var flag = Flag(fixture, fixture.TeamId, fixture.RuntimeId, fixture.Now.AddMinutes(-30 + round * 5), GameMode.Awd);
                flag.SpecificationId = AwdRoundSpecificationId.FromRound(round).Value;
                flag.ValidStart = fixture.Now.AddMinutes(-30 + round * 5);
                flag.ValidUntil = flag.ValidStart.Value.AddMinutes(5);
                db.ChallengeFlags.Add(flag);
            }
            var outside = Flag(fixture, fixture.TeamId, fixture.RuntimeId, fixture.Now.AddHours(-3), GameMode.Awd);
            outside.SpecificationId = AwdRoundSpecificationId.FromRound(99).Value;
            outside.ValidStart = fixture.Now.AddHours(-3);
            outside.ValidUntil = fixture.Now.AddHours(-2);
            db.ChallengeFlags.Add(outside);
            await db.SaveChangesAsync(ct);
            var reader = new RuntimeFlagReader(db);
            var first = await reader.ReadAsync(new(fixture.RuntimeId, true, 0, 2, fixture.Now), ct);
            var second = await reader.ReadAsync(new(fixture.RuntimeId, true, 2, 2, fixture.Now), ct);
            await Assert.That(first.Total).IsEqualTo(5);
            await Assert.That(second.Total).IsEqualTo(5);
            await Assert.That(first.Items.Select(item => item.Flag.Id).Intersect(second.Items.Select(item => item.Flag.Id)).Count()).IsEqualTo(0);
            var runtime = await db.RuntimeInstances.SingleAsync(item => item.Id == fixture.RuntimeId, ct);
            runtime.StoppedAt = fixture.Now.AddMinutes(-10);
            await db.SaveChangesAsync(ct);
            // The round starting exactly at stop time belongs to the next lifetime.
            await Assert.That((await reader.ReadAsync(new(fixture.RuntimeId, true, 0, 10, fixture.Now), ct)).Total).IsEqualTo(3);
        }, ct);
    }

    [Test, Timeout(300_000)]
    public async Task Template_tests_use_the_requested_uuid_and_do_not_expose_template_static_flags(CancellationToken ct)
    {
        await Run(GameMode.Ctf, async (db, fixture) =>
        {
            var old = new TemplateTestRuntimeInstance { Id = Guid.NewGuid(), ChallengeId = fixture.ChallengeId,
                State = RuntimeState.Stopped, RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker,
                CreatedAt = fixture.Now.AddHours(-1), StoppedAt = fixture.Now.AddMinutes(-10) };
            var latest = new TemplateTestRuntimeInstance { Id = Guid.NewGuid(), ChallengeId = fixture.ChallengeId,
                State = RuntimeState.Running, RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker,
                CreatedAt = fixture.Now.AddMinutes(-5) };
            db.RuntimeInstances.AddRange(old, latest);
            var oldFlag = new RuntimeInstanceChallengeFlag { Id = Guid.NewGuid(), ChallengeId = fixture.ChallengeId,
                SpecificationKind = SpecificationKind.RuntimeInstance, SpecificationId = old.Id, Flag = "old-test",
                CreatedAt = old.CreatedAt, ValidUntil = old.StoppedAt };
            var latestFlag = new RuntimeInstanceChallengeFlag { Id = Guid.NewGuid(), ChallengeId = fixture.ChallengeId,
                SpecificationKind = SpecificationKind.RuntimeInstance, SpecificationId = latest.Id, Flag = "new-test", CreatedAt = latest.CreatedAt };
            db.ChallengeFlags.AddRange(oldFlag, latestFlag, new TemplateChallengeFlag { Id = Guid.NewGuid(),
                ChallengeId = fixture.ChallengeId, Flag = "static", CreatedAt = fixture.Now.AddHours(-2) });
            await db.SaveChangesAsync(ct);
            var reader = new RuntimeFlagReader(db);
            var oldPage = await reader.ReadAsync(new(old.Id, true, 0, 10, fixture.Now), ct);
            var newPage = await reader.ReadAsync(new(latest.Id, true, 0, 10, fixture.Now), ct);
            await Assert.That(oldPage.Items.Single().Flag.Id).IsEqualTo(oldFlag.Id);
            await Assert.That(newPage.Items.Single().Flag.Id).IsEqualTo(latestFlag.Id);
            await Assert.That((await reader.FindScopeAsync(old.Id, fixture.OwnerId, false, ct))!.CanManageTemplate).IsTrue();
            await Assert.That((await reader.FindScopeAsync(old.Id, fixture.OtherUserId, false, ct))!.CanManageTemplate).IsFalse();
        }, ct);
    }

    [Test, Timeout(300_000)]
    public async Task Shared_static_flags_and_pending_internal_team_reads_do_not_depend_on_public_lists(CancellationToken ct)
    {
        await Run(GameMode.Ctf, async (db, fixture) =>
        {
            var runtime = await db.RuntimeInstances.SingleAsync(item => item.Id == fixture.RuntimeId, ct);
            runtime.TeamId = null;
            var templateFlag = new TemplateChallengeFlag { Id = Guid.NewGuid(), ChallengeId = fixture.ChallengeId,
                Flag = "static", CreatedAt = fixture.Now.AddHours(-1) };
            var competitionFlag = new CompetitionChallengeFlag { Id = Guid.NewGuid(), CompetitionChallengeId = fixture.CompetitionChallengeId,
                Flag = "competition-static", CreatedAt = fixture.Now.AddMinutes(-50) };
            db.ChallengeFlags.AddRange(templateFlag, competitionFlag, Flag(fixture, fixture.TeamId, fixture.RuntimeId, fixture.Now.AddMinutes(-1), GameMode.Ctf));
            var team = await db.Teams.SingleAsync(item => item.Id == fixture.TeamId, ct);
            team.RegistrationStatus = TeamRegistrationStatus.Pending;
            await db.SaveChangesAsync(ct);
            var page = await new RuntimeFlagReader(db).ReadAsync(new(runtime.Id, true, 0, 10, fixture.Now), ct);
            await Assert.That(page.Total).IsEqualTo(2);
            await Assert.That(page.Items.All(item => item.Source == NoCTF.Application.Runtime.Flags.RuntimeFlagSource.Static)).IsTrue();
            var get = new GetTeam(new TeamRegistrationStore(db, Substitute.For<IPostCommitMessagePublisher>()));
            await Assert.That(await get.ExecuteAsync(fixture.CompetitionId, fixture.TeamId, true, true, ct)).IsNotNull();
            await Assert.That(await get.ExecuteAsync(fixture.CompetitionId, fixture.TeamId, false, false, ct)).IsNull();
        }, ct);
    }

    [Test, Arguments(GameMode.Koh), Arguments(GameMode.Awdp), Timeout(300_000)]
    public async Task Flagless_and_fix_target_runtimes_do_not_borrow_flags_from_their_attributed_team(GameMode mode, CancellationToken ct)
    {
        await Run(mode, async (db, fixture) =>
        {
            if (mode == GameMode.Awdp)
                db.RuntimeInstances.Add(new AwdpTargetRuntimeInstance { Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId,
                    CompetitionChallengeId = fixture.CompetitionChallengeId, TeamId = fixture.TeamId, State = RuntimeState.Running,
                    RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker, CreatedAt = fixture.Now.AddMinutes(-5) });
            db.ChallengeFlags.Add(Flag(fixture, fixture.TeamId, fixture.RuntimeId, fixture.Now.AddMinutes(-1), GameMode.Ctf));
            await db.SaveChangesAsync(ct);
            var runtimeId = mode == GameMode.Koh ? fixture.RuntimeId
                : await db.RuntimeInstances.Where(item => item.Purpose == RuntimePurpose.AwdpTarget).Select(item => item.Id).SingleAsync(ct);
            await Assert.That((await new RuntimeFlagReader(db).ReadAsync(new(runtimeId, true, 0, 10, fixture.Now), ct)).Total).IsEqualTo(0);
        }, ct);
    }

    [Test, Arguments(GameMode.Ctf), Arguments(GameMode.Awdp), Timeout(300_000)]
    public Task Delivery_keeps_team_scope_and_runtime_flag_lifetime(GameMode mode, CancellationToken ct) => Run(mode, async (db, fixture) =>
    {
        if (mode == GameMode.Ctf)
        {
            var definition = await db.Set<CtfChallengeDefinition>().SingleAsync(item => item.ChallengeId == fixture.ChallengeId, ct);
            definition.Runtime = new ContainerChallengeRuntimeTemplate { ChallengeId = fixture.ChallengeId,
                FlagSource = PersistedRuntimeFlagSource.PerTeam, Allocation = PersistedRuntimeAllocation.PerTeam,
                Services = [new() { Name = "main", Image = "alpine", FlagEnvironmentVariableName = "FLAG" }] };
        }
        var current = Flag(fixture, fixture.TeamId, fixture.RuntimeId, fixture.Now.AddMinutes(-1), mode);
        var foreign = Flag(fixture, fixture.OtherTeamId, Guid.NewGuid(), fixture.Now.AddMinutes(-1), mode);
        db.ChallengeFlags.AddRange(current, foreign);
        await db.SaveChangesAsync(ct);
        var reader = new NoCTF.Infrastructure.Competitions.Delivery.CompetitionDeliveryReader(db,
            new NoCTF.GameModes.Registration.ChallengeRuntimeTemplateCatalog());
        var query = new NoCTF.Application.Competitions.Delivery.CompetitionDeliveryQuery(fixture.CompetitionId,
            fixture.CompetitionChallengeId, fixture.TeamId, false, 0, 10, false, fixture.Now);
        var page = await reader.ReadAsync(query, ct);
        await Assert.That(page!.Total).IsEqualTo(1);
        await Assert.That(page.Items.Single().Flags.Single().Id).IsEqualTo(current.Id);
        await Assert.That(page.Items.Single().TeamName).IsEqualTo("Team");
        var runtime = await db.RuntimeInstances.SingleAsync(item => item.Id == fixture.RuntimeId, ct);
        runtime.State = RuntimeState.Stopped;
        await db.SaveChangesAsync(ct);
        page = await reader.ReadAsync(query, ct);
        await Assert.That(page!.Items.Single().Flags.Count).IsEqualTo(mode == GameMode.Ctf ? 1 : 0);
        if (mode == GameMode.Awdp)
            await Assert.That((await reader.ReadAsync(query with { IncludeHistory = true }, ct))!.Items.Single().Flags.Single().State).IsEqualTo(RuntimeFlagState.Expired);
        await Assert.That((await reader.ReadAsync(query with { TeamId = Guid.NewGuid() }, ct))!.Total).IsEqualTo(0);
    }, ct);

    [Test, Timeout(300_000)]
    public Task Delivery_reads_random_attachments_without_assigning_them_and_bank_flags_remain_template_scoped(CancellationToken ct) =>
        Run(GameMode.Ctf, async (db, fixture) =>
        {
            var attachments = Enumerable.Range(1, 2).Select(index => new ChallengeAttachment
            {
                Id = Guid.NewGuid(), ChallengeId = fixture.ChallengeId, CreatedAt = fixture.Now,
                File = new NoCTF.Domain.Storage.StoredFile { Id = Guid.NewGuid(), ObjectKey = $"delivery/{Guid.NewGuid():N}",
                    FileName = $"challenge-{index}.zip", ByteLength = 20, Sha256 = new byte[32], CreatedAt = fixture.Now }
            }).ToArray();
            db.AddRange(attachments);
            foreach (var attachment in attachments)
                db.ChallengeFlags.Add(new TemplateChallengeFlag { Id = Guid.NewGuid(), ChallengeId = fixture.ChallengeId,
                    Flag = attachment.FileName, FlagSha256 = ManageChallengeFlags.Hash(attachment.FileName),
                    SpecificationKind = SpecificationKind.Attachment, SpecificationId = attachment.Id, CreatedAt = fixture.Now });
            var assigned = new TeamChallengeFlag { Id = Guid.NewGuid(), CompetitionChallengeId = fixture.CompetitionChallengeId,
                TeamId = fixture.TeamId, Flag = "team-attachment-flag", FlagSha256 = ManageChallengeFlags.Hash("team-attachment-flag"),
                SpecificationKind = SpecificationKind.Attachment, SpecificationId = attachments[0].Id, CreatedAt = fixture.Now };
            db.ChallengeFlags.Add(assigned);
            await db.SaveChangesAsync(ct);
            var reader = new NoCTF.Infrastructure.Competitions.Delivery.CompetitionDeliveryReader(db,
                new NoCTF.GameModes.Registration.ChallengeRuntimeTemplateCatalog());
            var query = new NoCTF.Application.Competitions.Delivery.CompetitionDeliveryQuery(fixture.CompetitionId, null, null,
                false, 0, 10, false, fixture.Now);
            var countBefore = await db.ChallengeFlags.CountAsync(ct);
            var page = await reader.ReadAsync(query, ct);
            await Assert.That(page!.Total).IsEqualTo(2);
            var team = page.Items.Single(item => item.TeamId == fixture.TeamId);
            await Assert.That(team.Attachments.Single().Id).IsEqualTo(attachments[0].Id);
            await Assert.That(team.Flags.Single().Flag).IsEqualTo(assigned.Flag);
            await Assert.That(team.Flags.Single().AttachmentId).IsEqualTo(team.Attachments.Single().Id);
            var unassigned = page.Items.Single(item => item.TeamId == fixture.OtherTeamId);
            await Assert.That(unassigned.RandomAttachment).IsTrue();
            await Assert.That(unassigned.Attachments.Count).IsEqualTo(0);
            await Assert.That(unassigned.Flags.Count).IsEqualTo(0);
            var next = await reader.ReadAsync(query with { Offset = 1, Limit = 1 }, ct);
            await Assert.That(next!.Total).IsEqualTo(2);
            await Assert.That(next.Items.Count).IsEqualTo(1);
            await Assert.That(await db.ChallengeFlags.CountAsync(ct)).IsEqualTo(countBefore);
            var bank = await new NoCTF.Infrastructure.Challenges.Flags.ChallengeFlagManagementStore(db)
                .ListAsync(ChallengeFlagScope.Template(fixture.ChallengeId), fixture.OwnerId, false, false, ct);
            await Assert.That(bank!.Count).IsEqualTo(2);
            await Assert.That(bank.All(flag => flag.TeamId == null && flag.CompetitionChallengeId == null)).IsTrue();
            assigned.DeletedAt = fixture.Now;
            await db.SaveChangesAsync(ct);
            var current = await reader.ReadAsync(query with { TeamId = fixture.TeamId }, ct);
            await Assert.That(current!.Items.Single().Flags.Count).IsEqualTo(0);
            var history = await reader.ReadAsync(query with { TeamId = fixture.TeamId, IncludeHistory = true }, ct);
            await Assert.That(history!.Items.Single().Flags.Single().State).IsEqualTo(RuntimeFlagState.Deleted);
        }, ct);

    private static ChallengeFlag Flag(Fixture fixture, Guid teamId, Guid runtimeId, DateTimeOffset createdAt, GameMode mode)
    {
        ChallengeFlag flag = mode switch { GameMode.Ctf => new TeamChallengeFlag(), GameMode.Awd => new AwdRoundChallengeFlag(), _ => new RuntimeInstanceChallengeFlag() };
        flag.Id = Guid.NewGuid();
        flag.CompetitionChallengeId = fixture.CompetitionChallengeId;
        flag.TeamId = teamId;
        flag.Flag = flag.Id.ToString();
        flag.FlagSha256 = ManageChallengeFlags.Hash(flag.Flag);
        flag.CreatedAt = createdAt;
        flag.SpecificationKind = mode switch { GameMode.Ctf => SpecificationKind.RuntimeDefinition, GameMode.Awd => SpecificationKind.AwdRound, _ => SpecificationKind.RuntimeInstance };
        flag.SpecificationId = mode switch { GameMode.Ctf => fixture.CompetitionChallengeId, GameMode.Awd => AwdRoundSpecificationId.FromRound(1).Value, _ => runtimeId };
        if (mode == GameMode.Awd)
        {
            flag.ValidStart = createdAt;
            flag.ValidUntil = fixture.Now.AddMinutes(5);
        }
        return flag;
    }

    private static Task Run(GameMode mode, Func<NoCtfDbContext, Fixture, Task> test, CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase("runtime_flags").WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var fixture = new Fixture(DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        foreach (var (id, name) in new[] { (fixture.OwnerId, "owner"), (fixture.OtherUserId, "other") })
            db.Users.Add(new User { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@test.invalid",
                PasswordHash = "test", CreatedAt = fixture.Now, UpdatedAt = fixture.Now });
        Competition competition = mode switch { GameMode.Ctf => new CtfCompetition(), GameMode.Awd => new AwdCompetition(), GameMode.Awdp => new AwdpCompetition(), _ => new KohCompetition() };
        competition.Id = fixture.CompetitionId; competition.OwnerId = fixture.OwnerId; competition.Title = "Flags";
        competition.CreatedAt = fixture.Now; competition.UpdatedAt = fixture.Now;
        competition.ModeConfiguration = TestConfigurations.Competition(mode);
        competition.FlagDerivationSecret = new byte[32];
        db.Competitions.Add(competition);
        Challenge template = mode switch { GameMode.Ctf => new CtfChallenge(), GameMode.Awd => new AwdChallenge(), GameMode.Awdp => new AwdpChallenge(), _ => new KohChallenge() };
        template.Id = fixture.ChallengeId; template.OwnerId = fixture.OwnerId; template.Title = "Template";
        template.Direction = "Web"; template.Definition = TestConfigurations.Definition(mode); template.CreatedAt = fixture.Now; template.UpdatedAt = fixture.Now;
        db.Challenges.Add(template);
        CompetitionChallenge challenge = mode switch { GameMode.Ctf => new CtfCompetitionChallenge(), GameMode.Awd => new AwdCompetitionChallenge(), GameMode.Awdp => new AwdpCompetitionChallenge(), _ => new KohCompetitionChallenge() };
        challenge.Id = fixture.CompetitionChallengeId; challenge.CompetitionId = fixture.CompetitionId; challenge.ChallengeId = fixture.ChallengeId;
        challenge.Rules = TestConfigurations.Rules(mode); challenge.UpdatedAt = fixture.Now;
        db.CompetitionChallenges.Add(challenge);
        foreach (var (id, captain, name, token) in new[] { (fixture.TeamId, fixture.OwnerId, "Team", 'a'), (fixture.OtherTeamId, fixture.OtherUserId, "Other", 'b') })
            db.Teams.Add(new Team { Id = id, CompetitionId = fixture.CompetitionId, Name = name, CaptainId = captain,
                MemberIds = [captain], InvitationToken = new string(token, 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = fixture.Now });
        db.RuntimeInstances.Add(new PlayerRuntimeInstance { Id = fixture.RuntimeId, CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.CompetitionChallengeId, TeamId = fixture.TeamId, State = RuntimeState.Running,
            RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker,
            CreatedAt = fixture.Now.AddHours(-1), RunningAt = fixture.Now.AddHours(-1) });
        await db.SaveChangesAsync(ct);
        await test(db, fixture);
    });

    private sealed record Fixture(DateTimeOffset Now, Guid OwnerId, Guid OtherUserId, Guid CompetitionId, Guid ChallengeId,
        Guid CompetitionChallengeId, Guid TeamId, Guid OtherTeamId, Guid RuntimeId);
}
