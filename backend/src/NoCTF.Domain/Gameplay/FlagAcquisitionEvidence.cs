namespace NoCTF.Domain.Gameplay;

[Flags]
public enum FlagAcquisitionResource : short
{
    None = 0,
    Container = 1,
    Attachment = 2
}

public enum FlagAcquisitionScope : short
{
    NotApplicable,
    FormalStaticCtf
}

public enum FlagAcquisitionEvidenceSource : short
{
    Recorded,
    LegacySubmission
}

/// <summary>Fixed at admission; subsequent resource access must not change an earlier attempt.</summary>
public sealed class FlagAcquisitionEvidence
{
    public FlagAcquisitionScope Scope { get; set; }
    public FlagAcquisitionResource Required { get; set; }
    public FlagAcquisitionResource Acquired { get; set; }
    public FlagAcquisitionEvidenceSource Source { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public Guid? RuntimeInstanceId { get; set; }
    public DateTimeOffset? RuntimeStartedAt { get; set; }
    public Guid? AttachmentDownloadFactId { get; set; }
    public DateTimeOffset? AttachmentDownloadedAt { get; set; }

    public FlagAcquisitionEvidence Copy() => (FlagAcquisitionEvidence)MemberwiseClone();

    public GameplayFactFailureCode? MissingEvidence => Scope != FlagAcquisitionScope.FormalStaticCtf
        ? null
        : (Required & ~Acquired) switch
        {
            FlagAcquisitionResource.Container => GameplayFactFailureCode.StaticFlagWithoutContainer,
            FlagAcquisitionResource.Attachment => GameplayFactFailureCode.StaticFlagWithoutAttachment,
            FlagAcquisitionResource.Container | FlagAcquisitionResource.Attachment => GameplayFactFailureCode.StaticFlagWithoutContainerAndAttachment,
            _ => null
        };
}

public static class CheatIncidentFailures
{
    public static readonly GameplayFactFailureCode[] All =
    [
        GameplayFactFailureCode.ForeignTeamFlagDetected,
        GameplayFactFailureCode.StaticFlagWithoutContainer,
        GameplayFactFailureCode.StaticFlagWithoutAttachment,
        GameplayFactFailureCode.StaticFlagWithoutContainerAndAttachment
    ];

    public static bool IsIncident(GameplayFactFailureCode? code) => code is { } value && All.Contains(value);
}
