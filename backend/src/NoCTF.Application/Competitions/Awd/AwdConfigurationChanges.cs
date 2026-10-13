namespace NoCTF.Application.Competitions.Awd;

public sealed record AwdConfigurationChangeAudit(Guid ActorUserId, string? Reason)
{
    public bool HasValidReason => ActorUserId != Guid.Empty
        && !string.IsNullOrWhiteSpace(Reason) && Reason.Length <= 512;
}
