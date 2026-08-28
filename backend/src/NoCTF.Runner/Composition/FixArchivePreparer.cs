using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Options;

namespace NoCTF.Runner.Composition;

public sealed class FixArchivePreparer
{
    private readonly long maxExpandedBytes;
    private readonly int maxEntries;
    private readonly long maxSingleFileBytes;
    private readonly double maxCompressionRatio;

    public FixArchivePreparer(IOptions<FixVerificationOptions> configuredOptions)
    {
        var options = configuredOptions.Value;
        maxExpandedBytes = options.MaxExpandedBytes;
        maxEntries = options.MaxArchiveEntries;
        maxSingleFileBytes = options.MaxSingleFileBytes;
        maxCompressionRatio = options.MaxCompressionRatio;
    }

    public async Task PrepareTarAsync(Stream source, string fileName, string patchEntrypoint,
        string workDirectory, string tarPath,
        CancellationToken cancellationToken)
    {
        if (!fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Fix archive must use the .tar.gz format.");
        if (Directory.Exists(workDirectory))
            Directory.Delete(workDirectory, recursive: true);
        var payloadRoot = Path.Combine(workDirectory, "noctf", "fix");
        Directory.CreateDirectory(payloadRoot);
        int entryCount;
        try
        {
            entryCount = await ExtractTarGzipAsync(source, payloadRoot, cancellationToken);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Fix archive cannot be empty or truncated.", exception);
        }
        if (entryCount == 0)
            throw new InvalidDataException("Fix archive cannot be empty.");
        var entrypointPath = SafePath(payloadRoot, patchEntrypoint);
        if (!File.Exists(entrypointPath))
            throw new InvalidDataException(
                "Fix archive must contain the configured patch entrypoint at its exact path.");
        TarFile.CreateFromDirectory(workDirectory, tarPath, includeBaseDirectory: false);
    }

    private async Task<int> ExtractTarGzipAsync(
        Stream source,
        string root,
        CancellationToken cancellationToken)
    {
        var countedSource = new CountingReadStream(source);
        await using var payload = new GZipStream(countedSource, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new TarReader(payload, leaveOpen: true);
        long expanded = 0;
        var count = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
        {
            if (++count > maxEntries) throw new InvalidDataException("Fix archive contains too many entries.");
            if (entry.Format is not (TarEntryFormat.Ustar or TarEntryFormat.Pax))
                throw new InvalidDataException("Fix archive must use a POSIX tar format.");
            var normalizedName = entry.Name.Replace('\\', '/').TrimEnd('/');
            if (!names.Add(normalizedName))
                throw new InvalidDataException("Fix archive contains duplicate paths.");
            var path = SafePath(root, entry.Name);
            if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(path); continue; }
            if (entry.EntryType is not TarEntryType.RegularFile)
                throw new InvalidDataException("Fix archive contains a forbidden entry type.");
            if (entry.Length < 0 || entry.Length > maxSingleFileBytes)
                throw new InvalidDataException("Fix archive contains a file beyond the configured per-file limit.");
            expanded = checked(expanded + entry.Length);
            if (expanded > maxExpandedBytes) throw new InvalidDataException("Fix archive expands beyond the configured limit.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var output = File.Create(path);
            if (entry.DataStream is not null) await entry.DataStream.CopyToAsync(output, cancellationToken);
            if (countedSource.BytesRead > 0 && expanded / (double)countedSource.BytesRead > maxCompressionRatio)
                throw new InvalidDataException("Fix archive exceeds the configured compression ratio.");
        }
        return count;
    }

    private sealed class CountingReadStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            BytesRead = checked(BytesRead + read);
            return read;
        }
        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer);
            BytesRead = checked(BytesRead + read);
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            BytesRead = checked(BytesRead + read);
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static string SafePath(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.Contains('\\'))
            throw new InvalidDataException("Fix archive path is invalid.");
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Fix archive path traversal is forbidden.");
        return path;
    }
}

public sealed class FixVerificationOptions
{
    public const string SectionName = "FixVerification";

    public long MaxExpandedBytes { get; init; } = 268_435_456L;

    public int MaxArchiveEntries { get; init; } = 2048;

    public long MaxSingleFileBytes { get; init; } = 64L * 1024 * 1024;

    public double MaxCompressionRatio { get; init; } = 100d;
}

public sealed class FixVerificationOptionsValidator : IValidateOptions<FixVerificationOptions>
{
    public ValidateOptionsResult Validate(string? name, FixVerificationOptions options) =>
        options.MaxExpandedBytes > 0
        && options.MaxArchiveEntries > 0
        && options.MaxSingleFileBytes > 0
        && options.MaxCompressionRatio > 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "FixVerification archive extraction limits must be positive.");
}
