using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Runner.Messages;

public enum AwdpAttackProvisioningPlanState
{
    NotApplicable,
    Ready,
    Invalid
}

public sealed record AwdpAttackProvisioningPlan(
    AwdpAttackProvisioningPlanState State,
    ContainerRuntimeRequest? Definition = null);

public interface IAwdpAttackProvisioningPlanReader
{
    Task<AwdpAttackProvisioningPlan> ReadAsync(
        ProvisionContainerRuntime message,
        CancellationToken cancellationToken);
}

public sealed class AwdpAttackProvisioningPlanReader(IDbContextFactory<NoCtfDbContext> contexts)
    : IAwdpAttackProvisioningPlanReader
{
    private static readonly ChallengeRuntimeTemplateCatalog RuntimeTemplates = new();

    public async Task<AwdpAttackProvisioningPlan> ReadAsync(
        ProvisionContainerRuntime message,
        CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var runtime = await db.RuntimeInstances.AsNoTracking()
            .Where(candidate => candidate.Id == message.RuntimeInstanceId
                && candidate.State == RuntimeState.Provisioning
                && candidate.RunnerId == message.RunnerId)
            .Select(candidate => new
            {
                candidate.Purpose,
                candidate.CompetitionId,
                candidate.CompetitionChallengeId,
                candidate.TeamId
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (runtime is null)
            return new(AwdpAttackProvisioningPlanState.Invalid);
        if (runtime.Purpose != RuntimePurpose.AwdpAttack)
            return new(AwdpAttackProvisioningPlanState.NotApplicable);
        if (runtime.TeamId is not Guid teamId)
            return new(AwdpAttackProvisioningPlanState.Invalid);

        var target = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == runtime.CompetitionChallengeId
                && challenge.CompetitionId == runtime.CompetitionId
                && challenge.DeletedAt == null)
            .Join(
                db.Competitions.AsNoTracking(),
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { Challenge = challenge, Competition = competition })
            .Join(
                db.Challenges.AsNoTracking(),
                target => target.Challenge.ChallengeId,
                challenge => challenge.Id,
                (target, challenge) => new
                {
                    Template = challenge
                })
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null)
            return new(AwdpAttackProvisioningPlanState.Invalid);

        var flag = await db.ChallengeFlags.AsNoTracking()
            .Where(candidate => candidate.CompetitionChallengeId
                    == runtime.CompetitionChallengeId
                && candidate.TeamId == teamId
                && candidate.SpecificationKind == SpecificationKind.RuntimeInstance
                && candidate.SpecificationId == message.RuntimeInstanceId
                && candidate.ValidStart == null
                && candidate.ValidUntil == null
                && candidate.DeletedAt == null)
            .Select(candidate => candidate.Flag)
            .SingleOrDefaultAsync(cancellationToken);
        if (flag is null)
            return new(AwdpAttackProvisioningPlanState.Invalid);

        ChallengeRuntimeTemplate? template;
        try
        {
            template = RuntimeTemplates.Get(target.Template.Definition);
        }
        catch (Exception exception) when (exception is JsonException
            or GameModeConfigurationException
            or InvalidOperationException)
        {
            return new(AwdpAttackProvisioningPlanState.Invalid);
        }
        if (template is not { FlagSource: RuntimeFlagSource.PerTeam, Definition: ContainerRuntimeDefinition { Services.Count: 1 } definition }
            || definition.Services[0].FlagEnvironmentVariableName is not { Length: > 0 } variable
            || message.Definition.Services.Count != 1
            || message.Definition.Services[0].Environment?.TryGetValue(variable, out var injectedFlag) != true
            || !string.Equals(injectedFlag, flag, StringComparison.Ordinal))
            return new(AwdpAttackProvisioningPlanState.Invalid);

        return new(AwdpAttackProvisioningPlanState.Ready, message.Definition);
    }
}
