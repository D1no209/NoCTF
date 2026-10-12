using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoCTF.Hosting.ManagedRuntime;

public enum ManagedRuntimeFlavor { Official, Satori }
public enum ManagedRuntimeDiagnostic { HeapWalk, GenerationBreakdown, Fragmentation, PinnedObjectCount, FullGcNotifications, NoGcRegion }

public sealed record ManagedRuntimeIdentity(ManagedRuntimeFlavor ExpectedFlavor, ManagedRuntimeFlavor ActualFlavor,
    string Framework, string Version, string RuntimeIdentifier, string SourceRevision)
{
    public IReadOnlyList<ManagedRuntimeDiagnostic> UnsupportedDiagnostics => ActualFlavor == ManagedRuntimeFlavor.Satori
        ? [ManagedRuntimeDiagnostic.HeapWalk, ManagedRuntimeDiagnostic.GenerationBreakdown,
            ManagedRuntimeDiagnostic.Fragmentation, ManagedRuntimeDiagnostic.PinnedObjectCount,
            ManagedRuntimeDiagnostic.FullGcNotifications, ManagedRuntimeDiagnostic.NoGcRegion] : [];

    public static ManagedRuntimeIdentity Read(Assembly entry)
    {
        var attributes = entry.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (!attributes.TryGetValue("NoCtfRuntimeFlavor", out var configured)
            || !Enum.TryParse<ManagedRuntimeFlavor>(configured, ignoreCase: false, out var expected)
            || !Enum.IsDefined(expected))
            throw new InvalidOperationException("The executable has no valid managed-runtime build identity.");
        var actual = Detect(GC.GetConfigurationVariables());
        return new(expected, actual, RuntimeInformation.FrameworkDescription, Environment.Version.ToString(),
            RuntimeInformation.RuntimeIdentifier, attributes.GetValueOrDefault("NoCtfRuntimeSourceRevision") ?? "unknown");
    }

    public static ManagedRuntimeFlavor Detect(IReadOnlyDictionary<string, object> configuration)
        => configuration.TryGetValue("SatoriGC", out var value) && value is true
            ? ManagedRuntimeFlavor.Satori : ManagedRuntimeFlavor.Official;

    public void Validate()
    {
        if (ExpectedFlavor != ActualFlavor)
            throw new InvalidOperationException($"Managed runtime mismatch: expected {ExpectedFlavor}, detected {ActualFlavor}. Refusing to start.");
        if (ActualFlavor == ManagedRuntimeFlavor.Satori && RuntimeIdentifier is not ("linux-x64" or "win-x64"))
            throw new InvalidOperationException("This Satori build supports only linux-x64 and win-x64.");
    }

    public string ToJson() => JsonSerializer.Serialize(this, ManagedRuntimeJsonContext.Default.ManagedRuntimeIdentity);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ManagedRuntimeIdentity))]
internal sealed partial class ManagedRuntimeJsonContext : JsonSerializerContext;
