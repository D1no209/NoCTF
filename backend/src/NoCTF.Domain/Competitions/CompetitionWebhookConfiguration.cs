namespace NoCTF.Domain.Competitions;

public sealed class CompetitionWebhookConfiguration
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<CompetitionWebhookTarget> Targets { get; set; } = [];
}

public sealed class CompetitionWebhookTarget
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public DateTimeOffset? EnabledAt { get; set; }
    public byte[] CurrentSecretCiphertext { get; set; } = [];
    public byte[]? PreviousSecretCiphertext { get; set; }
    public DateTimeOffset? PreviousSecretValidUntil { get; set; }
    public CompetitionWebhookDisabledReason? DisabledReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum CompetitionWebhookDisabledReason : short
{
    ReceiverGone
}
