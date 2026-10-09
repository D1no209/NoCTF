using System.Globalization;
using System.Text.RegularExpressions;
using NoCTF.Application.LiveSolo.Media;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed partial class LiveSoloCaptureFiles(LiveKitMediaOptions options) : ILiveSoloCaptureFiles
{
    private string DirectoryFor(Guid id) => Path.Combine(options.CaptureSpoolPath, "live-solo", id.ToString("N"));
    public async Task<IReadOnlyList<LiveSoloCapturedSegment>> SegmentsAsync(Guid id, CancellationToken ct)
    {
        var playlist = Path.Combine(DirectoryFor(id), "program.m3u8");
        if (!File.Exists(playlist)) return [];
        var entries = new List<LiveSoloCapturedSegment>(); double? seconds = null;
        foreach (var line in await File.ReadAllLinesAsync(playlist, ct))
        {
            if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            {
                var value = line[8..].Split(',')[0];
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) || !double.IsFinite(duration) || duration is <= 0 or > 30)
                    throw new InvalidDataException("Invalid media segment duration.");
                seconds = duration; continue;
            }
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
            var match = SegmentName().Match(line.Trim());
            if (!match.Success || seconds is null || !long.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var sequence))
                throw new InvalidDataException("Invalid media segment identity.");
            if (File.Exists(Path.Combine(DirectoryFor(id), line.Trim()))) entries.Add(new(sequence, line.Trim(), TimeSpan.FromSeconds(seconds.Value)));
            seconds = null;
        }
        return entries;
    }
    public Task<Stream?> OpenAsync(Guid id, string fileName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (fileName != "recording.mp4" && !SegmentName().IsMatch(fileName)) throw new InvalidDataException("Invalid media file identity.");
        var path = Path.Combine(DirectoryFor(id), fileName);
        return Task.FromResult<Stream?>(File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan) : null);
    }
    public Task RemoveAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(options.CaptureSpoolPath); var directory = Path.GetFullPath(DirectoryFor(id));
        if (!directory.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !Path.GetFileName(directory).Equals(id.ToString("N"), StringComparison.Ordinal)) throw new InvalidOperationException("Invalid capture directory.");
        if (!Directory.Exists(directory)) return Task.CompletedTask;
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var info = new FileInfo(file);
            if (info.LinkTarget is not null) throw new InvalidDataException("Capture files cannot be symbolic links.");
            File.Delete(file);
        }
        Directory.Delete(directory, recursive: false);
        return Task.CompletedTask;
    }
    [GeneratedRegex(@"^program_(\d+)\.ts$", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentName();
}
