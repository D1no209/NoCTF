using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Internal;
using NoCTF.Application.Authentication;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Integration.API;

[Category("Integration")]
public sealed class AwdCheckerCallbackHttpTests
{
    [Test, Timeout(300_000)]
    public async Task Signed_callback_scope_and_current_execution_control_task_reads_and_results(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Running, ct);
        var db = fixture.Db;
        var competitionId = Guid.NewGuid(); var challengeId = Guid.NewGuid(); var teamId = Guid.NewGuid();
        var runtimeId = Guid.NewGuid(); var factId = Guid.NewGuid(); var attemptId = Guid.NewGuid();
        var started = fixture.Now.AddSeconds(-1); var deadline = fixture.Now.AddMinutes(3);
        var round = new AwdRound { Id = Guid.NewGuid(), CompetitionId = competitionId, Number = 1,
            StartsAt = started, EndsAt = deadline, ActiveEndTicks = TimeSpan.FromMinutes(3).Ticks };
        var rules = new AwdRoundRules { Id = Guid.NewGuid(), CompetitionId = competitionId, CompetitionChallengeId = challengeId,
            RoundId = round.Id, EffectiveAt = started, CheckerIntervalSeconds = 60 };
        db.Competitions.Add(new AwdCompetition { Id = competitionId, OwnerId = fixture.OwnerId, Title = "Callback scope",
            Status = CompetitionStatus.Running, StartAt = started, EndAt = deadline,
            ModeConfiguration = CompetitionModeConfigurationDefaults.Create(GameMode.Awd, competitionId) });
        db.CompetitionChallenges.Add(new AwdCompetitionChallenge { Id = challengeId, CompetitionId = competitionId,
            ChallengeId = fixture.TemplateId, Rules = new AwdCompetitionChallengeRules(), IsPublished = true });
        db.Teams.Add(new Team { Id = teamId, CompetitionId = competitionId, CaptainId = fixture.OwnerId,
            MemberIds = [fixture.OwnerId], Name = "callback-team", InvitationToken = new string('c', 32), RegistrationStatus = TeamRegistrationStatus.Approved });
        db.RuntimeInstances.Add(new PlayerRuntimeInstance { Id = runtimeId, CompetitionId = competitionId,
            CompetitionChallengeId = challengeId, TeamId = teamId, State = RuntimeState.Running, RuntimeProvider = RuntimeProvider.Docker });
        db.AwdRounds.Add(round); db.AwdRoundRules.Add(rules);
        db.GameplayFacts.Add(new CheckObservationGameplayFact { Id = factId, CompetitionId = competitionId,
            CompetitionChallengeId = challengeId, TeamId = teamId, State = GameplayFactState.Processing,
            OccurredAt = started, UpdatedAt = started, ReferenceKind = GameplayFactReferenceKind.AwdRound, ReferenceId = round.Id,
            Awd = new() { RulesId = rules.Id, RuntimeId = runtimeId, AttemptId = attemptId, Execution = AwdCheckExecution.Running,
                AttemptStartedAt = started, AttemptDeadline = deadline, Deadline = deadline, ReceiptDeadline = deadline.AddSeconds(30),
                ManifestComplete = true, ReceivedRoundId = round.Id, ReceivedRoundNumber = 1 } });
        await db.SaveChangesAsync(ct);

        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Authentication:SigningKey"] = key, ["RunnerScoring:SigningKey"] = key }).Build();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true; options.Assemblies = [typeof(ReadAwdCheckTaskEndpoint).Assembly];
            options.Filter = type => type == typeof(ReadAwdCheckTaskEndpoint) || type == typeof(RecordCheckResultEndpoint) || type == typeof(RecordCheckResultValidator);
        });
        builder.Services.SwaggerDocument(); builder.Services.AddNoCtfAuthentication(configuration);
        builder.Services.AddDbContext<NoCtfDbContext>(options => options.UseNpgsql(db.Database.GetConnectionString()));
        builder.Services.AddDbContextFactory<NoCtfDbContext>(options => options.UseNpgsql(db.Database.GetConnectionString()), ServiceLifetime.Scoped);
        builder.Services.AddScoped<NoCTF.Infrastructure.Challenges.Testing.TemplateCapabilityTestExecutionStore>();
        builder.Services.AddScoped<NoCTF.Application.Challenges.Testing.ITemplateCapabilityCheckResultStore>(services =>
            services.GetRequiredService<NoCTF.Infrastructure.Challenges.Testing.TemplateCapabilityTestExecutionStore>());
        builder.Services.AddSingleton(fixture.MessageBus);
        builder.Services.AddScoped<IPostCommitMessagePublisher, NoCTF.Infrastructure.Messaging.WolverinePostCommitMessagePublisher>();
        builder.Services.AddScoped<NoCTF.Application.Competitions.Events.ICompetitionEventRecorder, CompetitionEventStore>();
        builder.Services.AddScoped<IInternalResultStore, InternalResultStore>(); builder.Services.AddScoped<RecordInternalResult>();
        builder.Services.AddScoped<IAwdCheckerTaskReader, AwdCheckerTaskReader>();
        builder.Services.AddSingleton<IOptions<EmailVerificationProtectionOptions>>(Options.Create(new EmailVerificationProtectionOptions { EncryptionKey = key }));
        builder.Services.AddSingleton<PlatformSecretProtector>();
        builder.Services.AddSingleton<NoCTF.Application.Authentication.Mfa.IMfaCryptography, NoCTF.Infrastructure.Authentication.Mfa.MfaCryptography>();
        builder.Services.AddScoped<NoCTF.Application.Authentication.Mfa.IMfaAuthenticationStore, NoCTF.Infrastructure.Authentication.Mfa.MfaAuthenticationStore>();
        await using var server = builder.Build(); server.UseAuthentication(); server.UseAuthorization(); server.UseNoCtfEndpoints();
        await server.StartAsync(ct);
        using var client = new HttpClient { BaseAddress = new Uri(server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var issuer = new RunnerScoringTokenIssuer(Options.Create(new RunnerScoringOptions { SigningKey = key }));
        void Authorize(Guid targetRuntime, Guid targetAttempt) => client.DefaultRequestHeaders.Authorization = new("Bearer",
            issuer.IssueAwdChecker(new("callback-runner", targetRuntime, factId, deadline, fixture.Now, targetAttempt, deadline.AddSeconds(30))));
        const string taskPath = "/api/internal/v1/awd/check-task"; const string resultPath = "/api/internal/v1/capabilities/check-results";
        var observed = DateTimeOffset.UtcNow;
        object Report(DateTimeOffset at) => new
        {
            probes = new[] { new { kind = "Connection", result = "TargetFailure", reason = "ConnectionRefused" },
                new { kind = "Functionality", result = "Unknown", reason = "None" } }, observedAt = at
        };
        await Assert.That((await client.GetAsync(taskPath, ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issuer.Issue("callback-runner", fixture.Now));
        await Assert.That((await client.GetAsync(taskPath, ct)).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        Authorize(runtimeId, Guid.NewGuid());
        await Assert.That((await client.GetAsync(taskPath, ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await client.PostAsJsonAsync(resultPath, Report(observed), ct)).StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        Authorize(Guid.NewGuid(), attemptId);
        await Assert.That((await client.PostAsJsonAsync(resultPath, Report(observed), ct)).StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        Authorize(runtimeId, attemptId);
        using (var task = await client.GetAsync(taskPath + $"?gameplayFactId={Guid.NewGuid()}", ct))
        {
            await Assert.That(task.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(task.Headers.CacheControl!.NoStore).IsTrue();
            var body = await task.Content.ReadFromJsonAsync<AwdCheckTaskResponse>(ct);
            await Assert.That(body!.GameplayFactId).IsEqualTo(factId); await Assert.That(body.AttemptId).IsEqualTo(attemptId);
        }
        await Assert.That((await client.PostAsJsonAsync(resultPath, Report(deadline.AddSeconds(1)), ct)).StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That((await client.PostAsJsonAsync(resultPath, Report(observed), ct)).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await client.PostAsJsonAsync(resultPath, Report(observed), ct)).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await client.GetAsync(taskPath, ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        db.ChangeTracker.Clear();
        var fact = await db.GameplayFacts.Include(value => value.Awd).AsSplitQuery().SingleAsync(value => value.Id == factId, ct);
        await Assert.That(fact.Result).IsEqualTo(GameplayFactResult.ServiceDown);
        await Assert.That(fact.Awd!.Settlement).IsEqualTo(AwdSettlementState.Final);
        await Assert.That(fact.CapabilityOutcomes.OfType<CheckOutcome>()).HasSingleItem();
        await Assert.That(await db.CompetitionEvents.CountAsync(value => value.CompetitionId == competitionId, ct)).IsEqualTo(1);
    }
}
