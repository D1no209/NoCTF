using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Runner.Messages;

public enum AwdpAttackProvisioningPlanState
{
    NotApplicable,
    Ready,
    Invalid
}

public sealed record AwdpAttackFileInjection(string Path, string Flag);

public sealed record AwdpAttackProvisioningPlan(
    AwdpAttackProvisioningPlanState State,
    ContainerRequest? Definition = null,
    AwdpAttackFileInjection? FileInjection = null);

public interface IAwdpAttackProvisioningPlanReader
{
    Task<AwdpAttackProvisioningPlan> ReadAsync(
        ProvisionContainerRuntime message,
        CancellationToken cancellationToken);
}

public sealed class AwdpAttackProvisioningPlanReader(IServiceScopeFactory scopes)
    : IAwdpAttackProvisioningPlanReader
{
    public async Task<AwdpAttackProvisioningPlan> ReadAsync(
        ProvisionContainerRuntime message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var runtime = await db.RuntimeInstances.AsNoTracking()
            .Where(candidate => candidate.Id == message.RuntimeInstanceId
                && candidate.Generation == message.Generation
                && candidate.ProcessingVersion == message.ProcessingVersion
                && candidate.State == RuntimeState.Provisioning
                && candidate.RunnerPool == message.RunnerPool
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
                    target.Competition.ConfigurationJson,
                    target.Challenge.RulesJson,
                    challenge.DefinitionJson
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null)
            return new(AwdpAttackProvisioningPlanState.Invalid);

        var flag = await db.ChallengeFlags.AsNoTracking()
            .Where(candidate => candidate.CompetitionChallengeId
                    == runtime.CompetitionChallengeId
                && candidate.TeamId == teamId
                && candidate.SpecificationKind == SpecificationKind.RuntimeGeneration
                && candidate.SpecificationId == message.RuntimeInstanceId
                && candidate.ValidStart == null
                && candidate.ValidUntil == null
                && candidate.DeletedAt == null)
            .Select(candidate => candidate.Flag)
            .SingleOrDefaultAsync(cancellationToken);
        if (flag is null)
            return new(AwdpAttackProvisioningPlanState.Invalid);

        AwdpEffectiveConfiguration configuration;
        try
        {
            configuration = AwdpConfigurationResolver.Resolve(
                target.ConfigurationJson,
                target.RulesJson,
                target.DefinitionJson);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return new(AwdpAttackProvisioningPlanState.Invalid);
        }
        if (configuration.FlagInjection is not { } injection)
            return new(AwdpAttackProvisioningPlanState.Invalid);

        return injection.Kind switch
        {
            AwdpFlagInjectionKind.EnvironmentVariable
                when !string.IsNullOrWhiteSpace(injection.EnvironmentVariableName) =>
                new(
                    AwdpAttackProvisioningPlanState.Ready,
                    message.Definition with
                    {
                        Environment = MergeEnvironment(
                            message.Definition.Environment,
                            injection.EnvironmentVariableName,
                            flag)
                    }),
            AwdpFlagInjectionKind.File
                when !string.IsNullOrWhiteSpace(injection.FilePath) =>
                new(
                    AwdpAttackProvisioningPlanState.Ready,
                    message.Definition,
                    new(injection.FilePath, flag)),
            _ => new(AwdpAttackProvisioningPlanState.Invalid)
        };
    }

    private static IReadOnlyDictionary<string, string> MergeEnvironment(
        IReadOnlyDictionary<string, string> source,
        string name,
        string value)
    {
        var result = source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        result[name] = value;
        return result;
    }
}
