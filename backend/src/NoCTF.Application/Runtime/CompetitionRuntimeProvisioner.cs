using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Runtime;

public sealed record RuntimeChallengeDefinition(
    Guid ChallengeId,
    int Order,
    int ConfigurationRevision,
    string ConfigurationJson);

public sealed record CompetitionRuntimeProvisioningSnapshot(
    Guid CompetitionId,
    GameMode Mode,
    CompetitionStatus Status,
    IReadOnlyList<RuntimeChallengeDefinition> Challenges,
    IReadOnlyList<Guid> ApprovedTeamIds);

public interface ICompetitionRuntimeProvisioningStore
{
    Task<CompetitionRuntimeProvisioningSnapshot?> LoadAsync(
        Guid competitionId,
        CancellationToken cancellationToken);
}

public sealed record CompetitionRuntimeProvisioningResult(
    int AttemptedCount,
    int ProvisionedCount,
    int FailedCount);

public sealed class CompetitionRuntimeProvisioner(
    ICompetitionRuntimeProvisioningStore store,
    IChallengeRuntimeTemplateCatalog templates,
    ChallengeRuntimeProvisioner challengeProvisioner)
{
    private static readonly ContainerResourceLimits DefaultLimits = new(268_435_456, 500_000_000, 128);
    private static readonly ContainerSecurityPolicy DefaultSecurity = new(true, true, true, ["ALL"], []);

    public async Task<CompetitionRuntimeProvisioningResult> ExecuteAsync(
        Guid competitionId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await store.LoadAsync(competitionId, cancellationToken);
        if (snapshot is null || snapshot.Status != CompetitionStatus.Running)
            return new(0, 0, 0);

        var attempted = 0;
        var provisioned = 0;
        var failed = 0;
        foreach (var challenge in snapshot.Challenges.OrderBy(item => item.Order).ThenBy(item => item.ChallengeId))
        {
            var template = templates.Get(snapshot.Mode, challenge.ConfigurationJson);
            if (template is null) continue;
            var subjects = template.Allocation == RuntimeAllocation.Shared
                ? new Guid?[] { null }
                : snapshot.ApprovedTeamIds.Order().Select(teamId => (Guid?)teamId).ToArray();
            foreach (var teamId in subjects)
            {
                attempted++;
                var result = await challengeProvisioner.ExecuteAsync(
                    BuildCommand(snapshot.CompetitionId, challenge, teamId, template),
                    cancellationToken);
                if (result.Status is NoCTF.Domain.Runtime.RuntimeStatus.Running
                    or NoCTF.Domain.Runtime.RuntimeStatus.Starting
                    or NoCTF.Domain.Runtime.RuntimeStatus.Pending
                    || result.AlreadyCompleted)
                    provisioned++;
                else
                    failed++;
            }
        }
        return new(attempted, provisioned, failed);
    }

    private static ProvisionChallengeRuntimeCommand BuildCommand(
        Guid competitionId,
        RuntimeChallengeDefinition challenge,
        Guid? teamId,
        ChallengeRuntimeTemplate template)
    {
        var environment = new Dictionary<string, string>(template.Environment ?? new Dictionary<string, string>(), StringComparer.Ordinal)
        {
            ["NOCTF_COMPETITION_ID"] = competitionId.ToString("N"),
            ["NOCTF_CHALLENGE_ID"] = challenge.ChallengeId.ToString("N")
        };
        if (teamId is Guid id) environment["NOCTF_TEAM_ID"] = id.ToString("N");
        var labels = new Dictionary<string, string>(template.Labels ?? new Dictionary<string, string>(), StringComparer.Ordinal)
        {
            ["noctf.io/competition-id"] = competitionId.ToString("N"),
            ["noctf.io/challenge-id"] = challenge.ChallengeId.ToString("N")
        };
        if (teamId is Guid labelTeamId) labels["noctf.io/team-id"] = labelTeamId.ToString("N");

        var subject = teamId?.ToString("N") ?? "shared";
        var operationKey = $"runtime:{challenge.ChallengeId:N}:{subject}:revision:{challenge.ConfigurationRevision}";
        var container = new ContainerRequest(
            Guid.Empty,
            template.Provider,
            template.Image,
            template.Command ?? [],
            environment,
            labels,
            template.PortMappings ?? new Dictionary<int, int>(),
            template.Limits ?? DefaultLimits,
            template.Security ?? DefaultSecurity,
            template.TtlSeconds is int ttl ? TimeSpan.FromSeconds(ttl) : null);
        return new(
            competitionId,
            challenge.ChallengeId,
            teamId,
            operationKey,
            container,
            template.OperationTimeoutSeconds is int timeout ? TimeSpan.FromSeconds(timeout) : null);
    }
}
