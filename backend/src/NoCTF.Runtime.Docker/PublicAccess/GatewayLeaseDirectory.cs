using System.Globalization;
using System.Text.Json;
using CliWrap;

namespace NoCTF.Runtime.Docker.PublicAccess;

/// <summary>
/// Private Linux bind directories. Renewals are short atomic file replacements, never Docker execs.
/// The trusted Runner may own the root; relays only mount their own listen directory and read-only lease.
/// </summary>
public sealed class GatewayLeaseDirectory
{
    private readonly string localRoot;
    private readonly string hostRoot;
    private readonly bool rootController;
    public Guid SessionId { get; } = Guid.NewGuid();
    public string User { get; }
    public string LocalSession => Path.Combine(localRoot, SessionId.ToString("D"));
    public string HostSession => hostRoot + "/" + SessionId.ToString("D");

    public GatewayLeaseDirectory(string localRoot, string hostRoot)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Shared SSH gateway requires Linux bind directories.");
        if (!SafeRoot(localRoot) || !SafeRoot(hostRoot)) throw new ArgumentException("Gateway state roots must be absolute Linux directories other than /.");
        this.localRoot = Path.GetFullPath(localRoot).TrimEnd('/');
        this.hostRoot = hostRoot.TrimEnd('/');
        var status = File.ReadAllLines("/proc/self/status");
        static uint Effective(string[] lines, string key) => uint.Parse(lines.Single(line => line.StartsWith(key, StringComparison.Ordinal))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[2], CultureInfo.InvariantCulture);
        var uid = Effective(status, "Uid:");
        var gid = Effective(status, "Gid:");
        rootController = uid == 0;
        // Never run the relay/client as root, even when the trusted Runner itself is root.
        User = rootController ? "65532:65532" : FormattableString.Invariant($"{uid}:{gid}");
        Directory.CreateDirectory(this.localRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if ((File.GetAttributes(this.localRoot) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Gateway state root must not be a symbolic link.");
        if ((File.GetUnixFileMode(this.localRoot) & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            throw new ArgumentException("Gateway state root must not be writable by other users or groups.");
        Directory.CreateDirectory(LocalSession, DirectoryMode);
    }

    private static UnixFileMode DirectoryMode => UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    private static bool SafeRoot(string value) => value.StartsWith('/') && value.Length > 1 && value.Length < 512
        && value.Split('/').All(segment => segment is not ("." or "..")) && !value.Any(char.IsControl);
    public string LocalPublication(Guid id) => Path.Combine(LocalSession, id.ToString("D"));
    public string HostPublication(Guid id) => HostSession + "/" + id.ToString("D");

    public async Task CreateAsync(Guid id, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var directory = LocalPublication(id);
        if (Directory.Exists(directory)) throw new IOException("Publication directory already exists.");
        Directory.CreateDirectory(directory, DirectoryMode);
        Directory.CreateDirectory(Path.Combine(directory, "control"), DirectoryMode);
        var listen = Path.Combine(directory, "listen");
        Directory.CreateDirectory(listen, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (rootController)
        {
            // One initialization operation, not a recurring probe. The path is a freshly created UUID
            // directory under the private root, not a user-provided shell fragment.
            await Cli.Wrap("chown").WithArguments(["--no-dereference", User, "--", listen]).ExecuteAsync(ct);
        }
    }

    public async Task RenewAsync(Guid id, DateTimeOffset expires, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var directory = Path.Combine(LocalPublication(id), "control");
        var next = Path.Combine(directory, "lease.next");
        await File.WriteAllBytesAsync(next, JsonSerializer.SerializeToUtf8Bytes(new { publicationId = id.ToString("D"), expiresAtUnixMs = expires.ToUnixTimeMilliseconds() }), ct);
        File.SetUnixFileMode(next, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        File.Move(next, Path.Combine(directory, "lease.json"), overwrite: true);
    }

    public void Invalidate(Guid id)
    {
        try { File.Delete(Path.Combine(LocalPublication(id), "control", "lease.json")); }
        catch (DirectoryNotFoundException) { /* A successor already removed this immutable publication. */ }
    }

    /// <summary>Call only after the immutable labeled helper has stopped; never remove the shared state root.</summary>
    public void Remove(Guid sessionId, Guid publicationId)
    {
        var session = Path.Combine(localRoot, sessionId.ToString("D"));
        var directory = Path.Combine(session, publicationId.ToString("D"));
        foreach (var path in new[] { localRoot, session, directory })
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Refusing to clean a symbolic gateway directory.");
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
