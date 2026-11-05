using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Application.Challenges.Bank;

public enum ChallengeTemplateCopyFailure : short { NotFound, Forbidden, UnsupportedSource, AttachmentsForbidden, FlagsForbidden, Conflict, AttachmentsRequired }
public sealed record CopyChallengeTemplateCommand(Guid SourceChallengeId, Guid ActorId,
    bool CopyAttachments, bool CopyFlags, DateTimeOffset Now);
public sealed record ChallengeTemplateCopyView(Guid ChallengeId, Guid CanonicalChallengeId,
    string Title, int AttachmentCount, int FlagCount);
public sealed record ChallengeTemplateCopyResult(ChallengeTemplateCopyView? Copy, ChallengeTemplateCopyFailure? Failure = null);
public interface IChallengeTemplateCopyStore
{
    Task<ChallengeTemplateCopyResult> CopyAsync(CopyChallengeTemplateCommand command, CancellationToken ct);
}
public sealed class CopyChallengeTemplate(IChallengeTemplateCopyStore store)
{
    public Task<ChallengeTemplateCopyResult> ExecuteAsync(CopyChallengeTemplateCommand command, CancellationToken ct)
    {
        if (command.SourceChallengeId == Guid.Empty) return Task.FromResult(new ChallengeTemplateCopyResult(null, ChallengeTemplateCopyFailure.UnsupportedSource));
        return store.CopyAsync(command, ct);
    }
}

public static class ChallengeTemplateCopyPolicy
{
    public static bool Supports(ChallengeDefinition? definition) => definition is not null
        && NoCTF.Application.Challenges.Capabilities.ChallengeCapabilityValidation.Validate(definition).Count == 0;

    public static ChallengeDefinition Copy(ChallengeDefinition source, Guid id)
    {
        if (!Supports(source)) throw new ArgumentException("A valid capability definition is required.", nameof(source));
        var definition = new ChallengeDefinition { ChallengeId = id,
            Capabilities = source.Capabilities.Select(ChallengeCapabilityCopy.Copy).ToList(),
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
                Commands = service.Commands.Select(x => new ChallengeRuntimeServiceCommand { IsArgument = x.IsArgument, Position = x.Position, Value = x.Value }).ToList(),
                Environment = service.Environment.Select(x => new ChallengeRuntimeServiceEnvironment { Name = x.Name, Value = x.Value }).ToList(),
                InternalPorts = service.InternalPorts.Select(x => new ChallengeRuntimeServicePort { Port = x.Port }).ToList() }).ToList() },
            OvaChallengeRuntimeTemplate ova => new OvaChallengeRuntimeTemplate { OvaSourceUrl = ova.OvaSourceUrl, Sha256 = ova.Sha256, HasExplicitLimits = ova.HasExplicitLimits,
                Limits = new() { MemoryBytes = ova.Limits.MemoryBytes, CpuMillicores = ova.Limits.CpuMillicores, PidsLimit = ova.Limits.PidsLimit } },
            _ => throw new ArgumentOutOfRangeException(nameof(source), "Unknown Runtime definition.")
        };
        result.OperationTimeoutSeconds = source.OperationTimeoutSeconds; result.EgressPolicy = source.EgressPolicy;
        result.UrlBindings = source.UrlBindings.Select(x => new ChallengeRuntimeUrlBinding { Position = x.Position,
            UrlTemplate = x.UrlTemplate, Exposure = x.Exposure, ContainerPort = x.ContainerPort, ServiceName = x.ServiceName, VmId = x.VmId, GuestPort = x.GuestPort }).ToList();
        return result;
    }
}
