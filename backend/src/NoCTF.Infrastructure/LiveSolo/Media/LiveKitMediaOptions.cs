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
    public string EgressOutputRoot { get; set; } = "/out";
    public string CaptureSpoolPath { get; set; } = "/out";
    public long RecordingQuotaBytes { get; set; } = 100L * 1024 * 1024 * 1024;
    public long RecordingExportLimitBytes { get; set; } = 512L * 1024 * 1024;
    public int ProgramChunkSeconds { get; set; } = 300;
}
