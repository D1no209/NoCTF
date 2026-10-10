using NoCTF.Domain.Challenges;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Templates;

public enum LiveSoloTemplateCopyFailure : short { NotFound, Forbidden, UnsupportedSource, AttachmentsForbidden, FlagsForbidden, Conflict, AttachmentsRequired, InvalidTags }
public sealed record CopyLiveSoloTemplateCommand(Guid CompetitionId, Guid SourceChallengeId, Guid ActorId,
    bool CopyAttachments, bool CopyFlags, IReadOnlyList<string>? Tags, DateTimeOffset Now);
public sealed record LiveSoloTemplateCopyView(Guid ChallengeId, Guid CompetitionChallengeId, Guid CanonicalChallengeId,
    string Title, int AttachmentCount, int FlagCount, bool AllocationChanged);
public sealed record LiveSoloTemplateCopyResult(LiveSoloTemplateCopyView? Copy, LiveSoloTemplateCopyFailure? Failure = null);
public interface ILiveSoloTemplateCopyStore
{
    Task<LiveSoloTemplateCopyResult> CopyAsync(CopyLiveSoloTemplateCommand command, CancellationToken ct);
}
public sealed class CopyLiveSoloTemplate(ILiveSoloTemplateCopyStore store)
{
    public Task<LiveSoloTemplateCopyResult> ExecuteAsync(CopyLiveSoloTemplateCommand command, CancellationToken ct)
    {
        if (command.SourceChallengeId == Guid.Empty) return Task.FromResult(new LiveSoloTemplateCopyResult(null, LiveSoloTemplateCopyFailure.UnsupportedSource));
        if (!CompetitionChallengeTags.TryNormalize(command.Tags, out var tags))
            return Task.FromResult(new LiveSoloTemplateCopyResult(null, LiveSoloTemplateCopyFailure.InvalidTags));
        return store.CopyAsync(command with { Tags = tags }, ct);
    }
}

/// <summary>Copies definitions, never references mutable source children or changes the original mode.</summary>
public static class LiveSoloTemplateCopyPolicy
{
    public static bool Supports(ChallengeDefinition? definition) => definition is CtfChallengeDefinition { InteractionKind: CtfInteractionKind.FlagSubmission }
        or LiveSoloChallengeDefinition && definition.Checker is null && definition.PatchEntrypoint is null
        && definition.PatchTimeoutSeconds is null && definition.MaximumPatchUploadBytes is null && !definition.CheckerFixInput
        && definition.StringItems.Count == 0 && definition.Runtime?.FlagSource != PersistedRuntimeFlagSource.AwdRotation;

    public static LiveSoloChallengeDefinition Copy(ChallengeDefinition source, Guid id)
    {
        if (!Supports(source)) throw new ArgumentException("Only FlagSubmission-compatible definitions can be copied.", nameof(source));
        var definition = new LiveSoloChallengeDefinition { ChallengeId = id, HasFlagTemplate = source.HasFlagTemplate,
            ReadyTimeoutSeconds = source.ReadyTimeoutSeconds,
            FlagTemplate = new() { Header = source.FlagTemplate.Header, BodyTemplate = source.FlagTemplate.BodyTemplate, LeetLiteralText = source.FlagTemplate.LeetLiteralText },
            Runtime = Runtime(source.Runtime) };
        ChallengeDefinitionGraph.AssignChallengeId(definition, id);
        return definition;
    }

    private static ChallengeRuntimeTemplateEntity? Runtime(ChallengeRuntimeTemplateEntity? source)
    {
        if (source is null) return null;
        ChallengeRuntimeTemplateEntity result = source switch
        {
            ContainerChallengeRuntimeTemplate container => new ContainerChallengeRuntimeTemplate { Services = container.Services.Select(service => new ChallengeRuntimeService {
                Name = service.Name, Position = service.Position, Image = service.Image, CpuCores = service.CpuCores, MemoryMiB = service.MemoryMiB,
                FlagEnvironmentVariableName = service.FlagEnvironmentVariableName,
                Commands = service.Commands.Select(x => new ChallengeRuntimeServiceCommand { IsArgument = x.IsArgument, Position = x.Position, Value = x.Value }).ToList(),
                Environment = service.Environment.Select(x => new ChallengeRuntimeServiceEnvironment { Name = x.Name, Value = x.Value }).ToList(),
                InternalPorts = service.InternalPorts.Select(x => new ChallengeRuntimeServicePort { Port = x.Port }).ToList() }).ToList() },
            OvaChallengeRuntimeTemplate ova => new OvaChallengeRuntimeTemplate { OvaSourceUrl = ova.OvaSourceUrl, Sha256 = ova.Sha256, HasExplicitLimits = ova.HasExplicitLimits,
                Limits = new() { MemoryBytes = ova.Limits.MemoryBytes, CpuMillicores = ova.Limits.CpuMillicores, PidsLimit = ova.Limits.PidsLimit } },
            _ => throw new ArgumentOutOfRangeException(nameof(source), "Unknown Runtime definition.")
        };
        result.Allocation = PersistedRuntimeAllocation.PerTeam; result.FlagSource = source.FlagSource;
        result.TtlSeconds = source.TtlSeconds; result.OperationTimeoutSeconds = source.OperationTimeoutSeconds; result.EgressPolicy = source.EgressPolicy;
        result.UrlBindings = source.UrlBindings.Select(x => new ChallengeRuntimeUrlBinding { Position = x.Position, IsControlCheck = x.IsControlCheck,
            UrlTemplate = x.UrlTemplate, Exposure = x.Exposure, ContainerPort = x.ContainerPort, ServiceName = x.ServiceName, VmId = x.VmId, GuestPort = x.GuestPort }).ToList();
        return result;
    }
}
