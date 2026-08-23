using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Awdp.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.GameplayFacts.Awdp;

public sealed class AwdpDefenseTargetStore(
    NoCtfDbContext db,
    GameplayFactAttemptCriticalSection criticalSection,
    IRuntimePlacementPolicy placementPolicy,
    ITransactionalMessageOutbox outbox,
    ICompetitionEventRecorder? eventRecorder = null) : IAwdpDefenseTargetStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<AwdpDefenseTargetRequestResult> TryCreateAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var scope = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && team.DeletedAt == null
                && !team.IsBanned
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && team.MemberIds.Contains(userId))
            .Select(team => new { TeamId = team.Id })
            .SingleOrDefaultAsync(cancellationToken);
        if (scope is null)
            return new(AwdpDefenseTargetRequestState.ScopeNotFound);

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        using var lease = await criticalSection.AcquireAsync(
            db,
            scope.TeamId,
            competitionChallengeId,
            GameplayFactKind.FixAttempt,
            cancellationToken);
        var teamIsStillEligible = await db.Teams.AsNoTracking().AnyAsync(team =>
            team.Id == scope.TeamId
            && team.CompetitionId == competitionId
            && team.DeletedAt == null
            && !team.IsBanned
            && team.RegistrationStatus == TeamRegistrationStatus.Approved
            && team.MemberIds.Contains(userId),
            cancellationToken);
        if (!teamIsStillEligible)
            return new(AwdpDefenseTargetRequestState.ScopeNotFound);

        var context = await db.CompetitionChallenges
            .Where(challenge => challenge.Id == competitionChallengeId
                && challenge.CompetitionId == competitionId
                && challenge.DeletedAt == null
                && challenge.IsPublished)
            .Join(
                db.Competitions.Where(competition => competition.Id == competitionId
                    && competition.DeletedAt == null
                    && competition.Mode == GameMode.Awdp
                    && competition.Status == CompetitionStatus.Running),
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { Challenge = challenge, Competition = competition })
            .Join(
                db.Challenges.Where(template => template.DeletedAt == null),
                item => item.Challenge.ChallengeId,
                template => template.Id,
                (item, template) => new
                {
                    item.Challenge,
                    item.Competition,
                    Template = template
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (context is null)
            return new(AwdpDefenseTargetRequestState.ScopeNotFound);

        var admission = await GameplayFactAdmissionPersistence.LoadAsync(
            db,
            competitionId,
            competitionChallengeId,
            userId,
            cancellationToken);
        if (admission is null)
            return new(AwdpDefenseTargetRequestState.ScopeNotFound);
        if (admission.HasCorrectFix)
            return new(AwdpDefenseTargetRequestState.AchievementAlreadySucceeded);

        var activeTargetExists = await db.RuntimeInstances.AnyAsync(instance =>
            instance.CompetitionId == competitionId
            && instance.CompetitionChallengeId == competitionChallengeId
            && instance.TeamId == scope.TeamId
            && instance.Purpose == RuntimePurpose.AwdpTarget
            && (instance.State == RuntimeState.Queued
                || instance.State == RuntimeState.Provisioning
                || instance.State == RuntimeState.Running
                || instance.State == RuntimeState.Stopping
                || instance.State == RuntimeState.Failed
                    && (instance.ProviderReceiptJson != null
                        || instance.RunnerAssignmentReleaseToken != null)),
            cancellationToken);
        if (activeTargetExists)
            return new(AwdpDefenseTargetRequestState.ActiveTargetExists);

        ChallengeRuntimeTemplate? template;
        RuntimePlacement placement;
        try
        {
            var configuration = AwdpConfigurationResolver.Resolve(
                context.Competition.ConfigurationJson,
                context.Challenge.RulesJson,
                context.Template.DefinitionJson);
            template = configuration.Runtime;
            if (template is null || configuration.Checker is null)
                return new(AwdpDefenseTargetRequestState.InvalidConfiguration);
            if (configuration.MaxFixSubmissions > 0
                && admission.AcceptedFixAttempts >= configuration.MaxFixSubmissions)
            {
                return new(AwdpDefenseTargetRequestState.AttemptsExhausted);
            }
            if (configuration.RequireBreakBeforeFix && !admission.HasCorrectBreak)
                return new(AwdpDefenseTargetRequestState.BreakRequired);
            placement = placementPolicy.Resolve(template.RuntimeKind);
        }
        catch (InvalidOperationException)
        {
            return new(AwdpDefenseTargetRequestState.InvalidConfiguration);
        }
        catch (GameModeConfigurationException)
        {
            return new(AwdpDefenseTargetRequestState.InvalidConfiguration);
        }

        var generation = checked((await db.RuntimeInstances
            .Where(instance => instance.CompetitionId == competitionId
                && instance.CompetitionChallengeId == competitionChallengeId
                && instance.TeamId == scope.TeamId
                && instance.Purpose == RuntimePurpose.AwdpTarget)
            .MaxAsync(instance => (int?)instance.Generation, cancellationToken) ?? 0) + 1);
        var runtimeInstanceId = Guid.CreateVersion7(now);
        try
        {
            _ = AwdpTargetDefinitionFactory.Create(
                runtimeInstanceId,
                generation,
                template,
                placement.Provider,
                now);
        }
        catch (InvalidOperationException)
        {
            return new(AwdpDefenseTargetRequestState.InvalidConfiguration);
        }

        var target = AwdpTargetRuntimeFactory.Create(
            scope.TeamId,
            null,
            competitionId,
            competitionChallengeId,
            runtimeInstanceId,
            template,
            placement,
            generation,
            now);
        db.RuntimeInstances.Add(target);
        await outbox.PublishAsync(new DispatchRuntime(target.Id));
        await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.RuntimeCreated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            now,
            ActorUserId: userId,
            TeamId: scope.TeamId,
            CompetitionChallengeId: competitionChallengeId,
            RuntimeInstanceId: target.Id,
            RuntimeState: target.State,
            RuntimeGeneration: target.Generation), cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return new(
                AwdpDefenseTargetRequestState.Created,
                target.Id,
                target.State);
        }
        catch (DbUpdateException)
        {
            return new(AwdpDefenseTargetRequestState.ConcurrencyConflict);
        }
    }
}
