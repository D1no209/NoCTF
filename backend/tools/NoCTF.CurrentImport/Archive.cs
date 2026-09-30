using System.Security.Cryptography;
using System.Text.Json;

namespace NoCTF.CurrentImport;

internal sealed record ManifestEntry(int Count, string Sha256);

internal sealed class MigrationArchive : IDisposable
{
    private static readonly HashSet<string> SupportedRecordSets =
    [
        "Files", "Users", "PlatformSettings", "Competitions", "Teams",
        "Challenges", "ChallengeAttachments", "CompetitionChallenges",
        "ChallengeFlags", "GameplayFacts", "RuntimeInstances", "PatchUploads",
        "CompetitionEvents", "Notifications", "AccountTokens", "DataProtectionKeys"
    ];
    private readonly JsonDocument document;
    private readonly byte[] plaintext;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private MigrationArchive(JsonDocument document, byte[] plaintext)
    {
        this.document = document;
        this.plaintext = plaintext;
        Version = document.RootElement.GetProperty("version").GetInt32();
        ExportedAt = document.RootElement.GetProperty("exportedAt").GetDateTimeOffset();
        if (Version != 1)
            throw new InvalidOperationException($"Unsupported migration archive version {Version}.");
        ValidateManifest();
    }

    public int Version { get; }
    public DateTimeOffset ExportedAt { get; }

    public static async Task<MigrationArchive> OpenAsync(
        string path,
        string keyPath,
        CancellationToken cancellationToken)
    {
        var package = await File.ReadAllBytesAsync(path, cancellationToken);
        if (package.Length < 37 || !package.AsSpan(0, 8).SequenceEqual("NCTFMIG1"u8))
            throw new InvalidOperationException("The migration archive header is invalid.");
        var key = Convert.FromBase64String((await File.ReadAllTextAsync(
            keyPath,
            cancellationToken)).Trim());
        if (key.Length != 32)
            throw new InvalidOperationException("The migration key must decode to 32 bytes.");
        var plaintext = new byte[package.Length - 36];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(
                package.AsSpan(8, 12),
                package.AsSpan(36),
                package.AsSpan(20, 16),
                plaintext,
                "NoCTF migration archive v1"u8);
            return new(JsonDocument.Parse(plaintext), plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(package);
        }
    }

    public IReadOnlyList<ArchiveRow> Rows(string name)
    {
        var rows = document.RootElement.GetProperty("records").GetProperty(name);
        return rows.EnumerateArray().Select(row => new ArchiveRow(row, jsonOptions)).ToArray();
    }

    private void ValidateManifest()
    {
        var records = document.RootElement.GetProperty("records");
        var manifest = document.RootElement.GetProperty("manifest");
        var recordNames = records.EnumerateObject().Select(entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);
        var manifestNames = manifest.EnumerateObject().Select(entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!recordNames.SetEquals(manifestNames)
            || !recordNames.SetEquals(SupportedRecordSets))
            throw new InvalidOperationException(
                "The migration archive has missing or unsupported record sets.");
        foreach (var entry in manifest.EnumerateObject())
        {
            var expected = entry.Value.Deserialize<ManifestEntry>(jsonOptions)
                ?? throw new InvalidOperationException($"Manifest entry {entry.Name} is invalid.");
            var rows = records.GetProperty(entry.Name);
            var actualBytes = System.Text.Encoding.UTF8.GetBytes(rows.GetRawText());
            var actualHash = Convert.ToHexString(SHA256.HashData(actualBytes));
            if (rows.GetArrayLength() != expected.Count
                || !string.Equals(actualHash, expected.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Manifest verification failed for {entry.Name}.");
            }
        }
    }

    public void Dispose()
    {
        document.Dispose();
        CryptographicOperations.ZeroMemory(plaintext);
    }
}

internal readonly struct ArchiveRow(JsonElement element, JsonSerializerOptions options)
{
    public JsonElement Element { get; } = element;

    public T Required<T>(string name) => Optional<T>(name)
        ?? throw new InvalidOperationException($"Required property {name} is missing in row {IdText}.");

    public T? Optional<T>(string name)
    {
        if (!Element.TryGetProperty(name, out var property)
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return default;
        return property.Deserialize<T>(options);
    }

    public string? Text(string name) => Optional<string>(name);
    public Guid Id => Required<Guid>("Id");
    public string IdText => Element.TryGetProperty("Id", out var id) ? id.ToString() : "(singleton)";
}
