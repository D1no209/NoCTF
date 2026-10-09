using System.Globalization;
using System.Text.RegularExpressions;
using NoCTF.Application.LiveSolo.Media;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed partial class LiveSoloCaptureFiles(LiveKitMediaOptions options) : ILiveSoloCaptureFiles
{
    private string DirectoryFor(Guid id)
    {
        var root = Path.GetFullPath(options.CaptureSpoolPath);
        var parent = Path.Combine(root, "live-solo");
        var directory = Path.Combine(parent, id.ToString("N"));
        foreach (var path in new[] { root, parent, directory })
            if (new DirectoryInfo(path).LinkTarget is not null)
                throw new InvalidDataException("Capture directories cannot be symbolic links.");
        return directory;
    }
    private static bool RegularFileExists(string path)
    {
        var file = new FileInfo(path);
        if (file.LinkTarget is not null) throw new InvalidDataException("Capture files cannot be symbolic links.");
        return file.Exists;
    }
    private string StagingDirectoryFor(string providerExportId)
    {
        if(!ProviderExportId().IsMatch(providerExportId))throw new InvalidDataException("Invalid provider export identity.");
        var root=Path.GetFullPath(options.CaptureSpoolPath);var parent=Path.Combine(root,".egress-tmp");
        var directory=Path.Combine(parent,providerExportId);
        foreach(var path in new[]{root,parent,directory})
            if(new DirectoryInfo(path).LinkTarget is not null)throw new InvalidDataException("Capture staging directories cannot be symbolic links.");
        return directory;
    }
    public Task<long?> RecordingLengthAsync(Guid id,string? providerExportId,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();var path=Path.Combine(DirectoryFor(id),"recording.mp4");
        long? length=RegularFileExists(path)?new FileInfo(path).Length:null;
        if(providerExportId is not null)
        {
            var staging=Path.Combine(StagingDirectoryFor(providerExportId),"recording.mp4");
            if(RegularFileExists(staging))length=Math.Max(length??0,new FileInfo(staging).Length);
        }
        return Task.FromResult(length);
    }
    public Task RemoveRecordingStagingAsync(string providerExportId,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();var directory=StagingDirectoryFor(providerExportId);
        if(!Directory.Exists(directory))return Task.CompletedTask;
        var recording=Path.Combine(directory,"recording.mp4");
        if(RegularFileExists(recording))File.Delete(recording);
        if(Directory.EnumerateFileSystemEntries(directory).Any())
            throw new IOException("Unexpected provider staging files require inspection.");
        Directory.Delete(directory,recursive:false);return Task.CompletedTask;
    }
    public async Task<IReadOnlyList<LiveSoloCapturedSegment>> SegmentsAsync(Guid id, CancellationToken ct)
    {
        var playlist = Path.Combine(DirectoryFor(id), "program.m3u8");
        if (!RegularFileExists(playlist)) return [];
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
            RegularFileExists(Path.Combine(DirectoryFor(id), line.Trim()));
            entries.Add(new(sequence, line.Trim(), TimeSpan.FromSeconds(seconds.Value)));
            seconds = null;
        }
        return entries;
    }
    public Task<Stream?> OpenAsync(Guid id, string fileName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (fileName != "recording.mp4" && !SegmentName().IsMatch(fileName)) throw new InvalidDataException("Invalid media file identity.");
        var path = Path.Combine(DirectoryFor(id), fileName);
        return Task.FromResult<Stream?>(RegularFileExists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan) : null);
    }
    public Task RemoveSegmentAsync(Guid id,string fileName,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(!SegmentName().IsMatch(fileName))throw new InvalidDataException("Invalid media segment identity.");
        var path=Path.Combine(DirectoryFor(id),fileName);
        if(RegularFileExists(path))File.Delete(path);
        return Task.CompletedTask;
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
    [GeneratedRegex(@"^EG_[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderExportId();
}
