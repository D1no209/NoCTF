using System.Net;
using System.Net.Http.Json;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Actions;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Actions;
using NoCTF.Infrastructure.Challenges.Hints;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Competitions.Koh;
using NoCTF.Infrastructure.Competitions.Progression;
using NoCTF.Infrastructure.GameplayFacts.Intake;

namespace NoCTF.Tests.Integration.API;

[Category("Integration")]
public sealed class ChallengeAudienceEndpointTests
{
    [Test, Timeout(300_000)]
    public async Task Ineligible_actor_cannot_list_or_get_published_challenges(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Running, ct);
        var scope = await SeedAsync(fixture, ct);
        var team = await fixture.Db.Teams.SingleAsync(team => team.Id == scope.TeamId, ct);
        team.RegistrationStatus = TeamRegistrationStatus.Pending;
        await fixture.Db.SaveChangesAsync(ct);
        await using var app = await StartAsync(fixture, scope.ActorId, ct);
        using var client = app.GetTestClient();
        using var list = await client.GetAsync($"/api/v1/competitions/{fixture.CompetitionId}/challenges", ct);
        using var detail = await client.GetAsync(Path(fixture, scope.ChallengeId), ct);
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test, Timeout(300_000)]
    public async Task Eligible_actor_reads_hints_attempts_and_available_break_from_the_same_team(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Running, ct);
        var scope = await SeedAsync(fixture, ct);
        var fact = new BreakAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = scope.ChallengeId, TeamId = scope.TeamId, ActorUserId = scope.ActorId,
            Value = "flag{wrong}", OccurredAt = fixture.Now, UpdatedAt = fixture.Now, State = GameplayFactState.Completed, Result = GameplayFactResult.Wrong };
        var outcome = new NoCTF.Application.Challenges.Capabilities.EvaluateBreak().Execute(new(fact.Id, fact.Value, [], fixture.Now));
        fact.CapabilityOutcomes.Add(outcome.Value!.ToGameplayOutcome());
        fixture.Db.GameplayFacts.Add(fact); await fixture.Db.SaveChangesAsync(ct);
        await using var app = await StartAsync(fixture, scope.ActorId, ct);
        using var client = app.GetTestClient();
        using var list = await client.GetAsync($"/api/v1/competitions/{fixture.CompetitionId}/challenges", ct);
        using var detail = await client.GetAsync(Path(fixture, scope.ChallengeId), ct);
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var response = (await detail.Content.ReadFromJsonAsync<ChallengeResponse>(ct))!;
        await Assert.That(response.RemainingFlagAttempts).IsEqualTo(2);
        await Assert.That(response.Hints!.Count).IsEqualTo(2);
        await Assert.That(response.Hints.Single(hint => hint.Cost == 0).Content).IsEqualTo("Public hint");
        await Assert.That(response.Hints.Single(hint => hint.Cost == 20).Content).IsNull();
        await Assert.That(response.AvailableActions.Single().Kind).IsEqualTo(ChallengeActionKind.SubmitFlag);
        await Assert.That(response.AvailableActions.Single().RemainingAttempts).IsEqualTo(2);
        await Assert.That(detail.Headers.CacheControl!.NoStore).IsTrue();
        await Assert.That(detail.Headers.CacheControl.Private).IsTrue();
    }

    [Test, Timeout(300_000)]
    public async Task Detail_reads_current_progression_access_without_a_start_request(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Running, ct);
        var scope = await SeedAsync(fixture, ct);
        var node = new ChallengeProgressionNode { Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId, CompetitionChallengeId = scope.ChallengeId };
        fixture.Db.CompetitionProgressions.Add(new() { CompetitionId = fixture.CompetitionId, Enabled = true, Revision = 1, Nodes = [node] });
        var current = new TeamProgressionNodeState { CompetitionId = fixture.CompetitionId, TeamId = scope.TeamId, NodeId = node.Id, GraphRevision = 1, Active = true, EvaluatedAt = fixture.Now };
        fixture.Db.TeamProgressionNodeStates.Add(current); await fixture.Db.SaveChangesAsync(ct);
        await using var app = await StartAsync(fixture, scope.ActorId, ct);
        using var client = app.GetTestClient();
        using var opened = await client.GetAsync(Path(fixture, scope.ChallengeId), ct);
        await Assert.That(opened.StatusCode).IsEqualTo(HttpStatusCode.OK);
        current.Active = false; await fixture.Db.SaveChangesAsync(ct);
        using var closed = await client.GetAsync(Path(fixture, scope.ChallengeId), ct);
        await Assert.That(closed.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        current.Active = true; await fixture.Db.SaveChangesAsync(ct);
        using var reopened = await client.GetAsync(Path(fixture, scope.ChallengeId), ct);
        await Assert.That(reopened.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static string Path(ChallengeApplicationFixture fixture, Guid challengeId) => $"/api/v1/competitions/{fixture.CompetitionId}/challenges/{challengeId}";

    private static async Task<(Guid ActorId, Guid TeamId, Guid ChallengeId)> SeedAsync(ChallengeApplicationFixture fixture, CancellationToken ct)
    {
        var actor = Guid.NewGuid(); var team = Guid.NewGuid(); var challenge = Guid.NewGuid();
        fixture.Db.Users.Add(new() { Id = actor, UserName = "participant", NormalizedUserName = "PARTICIPANT", Email = "participant@example.test",
            NormalizedEmail = "PARTICIPANT@EXAMPLE.TEST", Role = UserRole.User, AccountStatus = UserAccountStatus.Active });
        fixture.Db.Teams.Add(new() { Id = team, CompetitionId = fixture.CompetitionId, Name = "Participants", NormalizedName = "PARTICIPANTS",
            CaptainId = actor, MemberIds = [actor], InvitationToken = Guid.NewGuid().ToString("N"), RegistrationStatus = TeamRegistrationStatus.Approved });
        fixture.Db.CompetitionChallenges.Add(new CtfCompetitionChallenge { Id = challenge, CompetitionId = fixture.CompetitionId, ChallengeId = fixture.TemplateId,
            IsPublished = true, UpdatedAt = fixture.Now, Rules = new CtfCompetitionChallengeRules { MaxFlagAttempts = 3 },
            Hints = [new() { Id = Guid.NewGuid(), Content = "Public hint", Cost = 0, PublishedAt = fixture.Now.AddMinutes(-1) },
                new() { Id = Guid.NewGuid(), Content = "Private hint", Cost = 20, PublishedAt = fixture.Now.AddMinutes(-1) },
                new() { Id = Guid.NewGuid(), Content = "Unpublished hint", Cost = 0 }] });
        await fixture.Db.SaveChangesAsync(ct); return (actor, team, challenge);
    }

    private static async Task<WebApplication> StartAsync(ChallengeApplicationFixture fixture, Guid actorId, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails(); builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options => { options.DisableAutoDiscovery = true; options.Assemblies = [typeof(GetChallengeEndpoint).Assembly];
            options.Filter = type => type == typeof(GetChallengeEndpoint) || type == typeof(ListChallengesEndpoint); });
        var templates = new ChallengeRuntimeTemplateCatalog();
        builder.Services.AddSingleton<IUserContext>(new ActorContext(actorId));
        builder.Services.AddSingleton<ICompetitionChallengeReadAccess>(new CompetitionChallengeReadAccess(fixture.Db));
        builder.Services.AddSingleton<IChallengeManagementStore>(fixture.Challenges);
        builder.Services.AddSingleton<IProgressionChallengeAccess>(new ProgressionChallengeAccess(fixture.Db));
        builder.Services.AddSingleton<IKohChallengeAccessReader>(new KohChallengeAccessReader(fixture.Db, templates));
        builder.Services.AddSingleton<IFlagAttemptStateReader>(new FlagAttemptStateReader(fixture.Db));
        builder.Services.AddSingleton<IParticipantChallengeHintStore>(new ParticipantChallengeHintStore(fixture.Db));
        builder.Services.AddSingleton<IAvailableChallengeActionsReader>(new AvailableChallengeActionsReader(fixture.Db, templates, new GameModeGameplayFactAdmissionPolicy()));
        builder.Services.AddScoped<GetAvailableChallengeActions>(); builder.Services.AddScoped<ReadParticipantChallengeHints>();
        builder.Services.AddScoped<GetFlagAttemptState>(); builder.Services.AddScoped<GetChallenge>(); builder.Services.AddScoped<ListChallenges>();
        var app = builder.Build(); app.UseNoCtfEndpoints(); await app.StartAsync(ct); return app;
    }

    private sealed class ActorContext(Guid actorId) : IUserContext
    {
        public Guid UserId => actorId;
        public bool IsAdministrator => false;
    }
}
