namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed class LiveKitMediaOptions
{
    public const string Section = "LiveSolo:Media";
    public bool Enabled { get; set; }
    public Uri? ApiUrl { get; set; }
    public Uri? ClientUrl { get; set; }
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public Uri? EgressHealthUrl { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 10;
    public int MaximumParticipants { get; set; } = 16;
}
