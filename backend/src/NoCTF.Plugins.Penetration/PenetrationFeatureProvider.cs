using System.Text.Json;
using NoCTF.Application.CompetitionModes;
using NoCTF.Infrastructure;

namespace NoCTF.Plugins.Penetration;

public sealed class PenetrationFeatureProvider(
    PenetrationInstanceService instanceService)
    : IChallengeFeatureProvider
{
    public string TypeId => PenetrationConstants.TypeId;

    public bool CanHandle(string featureKey)
        => featureKey is PenetrationConstants.PlayerDetail
            or PenetrationConstants.PlayerInstanceGet
            or PenetrationConstants.PlayerInstanceStart
            or PenetrationConstants.PlayerInstanceStop
            or PenetrationConstants.PlayerInstanceReset
            or PenetrationConstants.PlayerInstanceDestroy;

    public async Task<ChallengeFeatureResult> HandleAsync(ChallengeFeatureContext context, CancellationToken ct = default)
    {
        if (!context.TeamId.HasValue)
            return new ChallengeFeatureResult(false, "no_team", StatusCode: 400);

        object data = context.FeatureKey switch
        {
            PenetrationConstants.PlayerDetail => await instanceService.GetDetailAsync(context.CompetitionId, context.ChallengeId, context.TeamId.Value, ct),
            PenetrationConstants.PlayerInstanceGet => await instanceService.GetInstanceAsync(context.CompetitionId, context.ChallengeId, context.TeamId.Value, ct),
            PenetrationConstants.PlayerInstanceStart => await instanceService.StartAsync(context.CompetitionId, context.ChallengeId, context.TeamId.Value, context.UserId, ct),
            PenetrationConstants.PlayerInstanceStop => await instanceService.StopAsync(context.CompetitionId, context.ChallengeId, context.TeamId.Value, context.UserId, ct),
            PenetrationConstants.PlayerInstanceReset => await instanceService.ResetAsync(context.CompetitionId, context.ChallengeId, context.TeamId.Value, context.UserId, adminOverride: false, ct),
            PenetrationConstants.PlayerInstanceDestroy => await instanceService.DestroyAsync(context.CompetitionId, context.ChallengeId, context.TeamId.Value, context.UserId, adminOverride: false, ct),
            _ => throw new NotSupportedException(context.FeatureKey)
        };

        return new ChallengeFeatureResult(true, "ok", data);
    }
}

public sealed class PenetrationAdminFeatureProvider(
    PenetrationTopologyService topologyService,
    PenetrationInstanceService instanceService)
    : IChallengeAdminFeatureProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string TypeId => PenetrationConstants.TypeId;

    public bool CanHandle(string featureKey)
        => featureKey is PenetrationConstants.TemplateTopologyGet
            or PenetrationConstants.TemplateTopologyUpdate
            or PenetrationConstants.TemplateCloneToChallenge
            or PenetrationConstants.CompetitionTopologyGet
            or PenetrationConstants.CompetitionTopologyUpdate
            or PenetrationConstants.AdminInstancesList
            or PenetrationConstants.AdminInstanceGet
            or PenetrationConstants.AdminInstanceReset
            or PenetrationConstants.AdminInstanceDestroy;

    public async Task<ChallengeFeatureResult> HandleAsync(ChallengeFeatureContext context, CancellationToken ct = default)
    {
        object data = context.FeatureKey switch
        {
            PenetrationConstants.TemplateTopologyGet => await topologyService.GetTemplateTopologyAsync(context.ChallengeId, ct),
            PenetrationConstants.TemplateTopologyUpdate => await topologyService.SaveTemplateTopologyAsync(
                context.ChallengeId,
                Deserialize<PenetrationTopologyDocument>(context.PayloadJson),
                ct),
            PenetrationConstants.TemplateCloneToChallenge => await CloneAsync(context.PayloadJson, ct),
            PenetrationConstants.CompetitionTopologyGet => await topologyService.GetCompetitionTopologyAsync(context.CompetitionId, context.ChallengeId, ct),
            PenetrationConstants.CompetitionTopologyUpdate => await topologyService.SaveCompetitionTopologyAsync(
                context.CompetitionId,
                context.ChallengeId,
                Deserialize<PenetrationTopologyDocument>(context.PayloadJson),
                ct),
            PenetrationConstants.AdminInstancesList => await ListInstancesAsync(context, ct),
            PenetrationConstants.AdminInstanceGet => await GetAdminInstanceAsync(context, ct),
            PenetrationConstants.AdminInstanceReset => await ResetAdminInstanceAsync(context, ct),
            PenetrationConstants.AdminInstanceDestroy => await DestroyAdminInstanceAsync(context, ct),
            _ => throw new NotSupportedException(context.FeatureKey)
        };

        return new ChallengeFeatureResult(true, "ok", data);
    }

    private Task<PenetrationTopologyDto> CloneAsync(string payloadJson, CancellationToken ct)
    {
        var request = Deserialize<PenetrationCloneRequest>(payloadJson);
        return topologyService.CloneTemplateToChallengeAsync(request.TemplateId, request.ChallengeId, ct);
    }

    private async Task<object> ListInstancesAsync(ChallengeFeatureContext context, CancellationToken ct)
    {
        var request = string.IsNullOrWhiteSpace(context.PayloadJson)
            ? new AdminInstanceListRequest()
            : Deserialize<AdminInstanceListRequest>(context.PayloadJson);
        return await instanceService.ListAdminInstancesAsync(context.CompetitionId, request.ChallengeId, request.TeamId, ct);
    }

    private async Task<object> GetAdminInstanceAsync(ChallengeFeatureContext context, CancellationToken ct)
    {
        var request = Deserialize<AdminInstanceRequest>(context.PayloadJson);
        var (instance, challenge) = await instanceService.LoadInstanceForAdminAsync(context.CompetitionId, request.InstanceId, ct);
        return new
        {
            id = instance.Id,
            instance.TeamId,
            instance.ChallengeId,
            challengeTitle = challenge.Title,
            status = instance.Status.ToString(),
            instance.EntryUrl,
            instance.EntryHost,
            instance.EntryPort,
            instance.ResetCount,
            instance.ExpiresAt,
            instance.LastError,
            instance.ContainerIdsJson,
            instance.PortMappingsJson,
            instance.CreatedAt,
            instance.UpdatedAt
        };
    }

    private async Task<object> ResetAdminInstanceAsync(ChallengeFeatureContext context, CancellationToken ct)
    {
        var request = Deserialize<AdminInstanceRequest>(context.PayloadJson);
        var (instance, _) = await instanceService.LoadInstanceForAdminAsync(context.CompetitionId, request.InstanceId, ct);
        return await instanceService.ResetAsync(context.CompetitionId, instance.ChallengeId, instance.TeamId, context.UserId, adminOverride: true, ct);
    }

    private async Task<object> DestroyAdminInstanceAsync(ChallengeFeatureContext context, CancellationToken ct)
    {
        var request = Deserialize<AdminInstanceRequest>(context.PayloadJson);
        var (instance, _) = await instanceService.LoadInstanceForAdminAsync(context.CompetitionId, request.InstanceId, ct);
        return await instanceService.DestroyAsync(context.CompetitionId, instance.ChallengeId, instance.TeamId, context.UserId, adminOverride: true, ct);
    }

    private static T Deserialize<T>(string payloadJson)
        => JsonSerializer.Deserialize<T>(
               string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
               JsonOptions)
           ?? throw new InvalidOperationException("invalid_payload");

    private sealed class AdminInstanceListRequest
    {
        public Guid? ChallengeId { get; set; }
        public Guid? TeamId { get; set; }
    }

    private sealed class AdminInstanceRequest
    {
        public Guid InstanceId { get; set; }
    }
}
