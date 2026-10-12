using NoCTF.Hosting.ManagedRuntime;

var identity = ManagedRuntimeIdentity.Read(typeof(Program).Assembly);
identity.Validate();
Console.WriteLine(identity.ToJson());
if (args.Contains("--runtime-info", StringComparer.Ordinal)) return;
if (args.Contains("--diagnostic-crash", StringComparer.Ordinal))
    Environment.FailFast("Intentional synthetic runtime diagnostic probe crash.");

var duration = TimeSpan.Zero;
var durationIndex = Array.IndexOf(args, "--duration-seconds");
if (durationIndex >= 0)
{
    if (durationIndex + 1 >= args.Length || !int.TryParse(args[durationIndex + 1], out var seconds)
        || seconds is < 1 or > 3600)
        throw new ArgumentException("--duration-seconds requires a value between 1 and 3600.");
    duration = TimeSpan.FromSeconds(seconds);
}
var elapsed = System.Diagnostics.Stopwatch.StartNew();

// Bounded allocation and cross-thread object identity smoke test, not a performance claim.
for (var pass = 0; pass < 20 || elapsed.Elapsed < duration; pass++)
{
    var arrays = Enumerable.Range(0, 32).Select(index => new byte[32_768 + index * 1024]).ToArray();
    await Parallel.ForEachAsync(arrays, (array, _) =>
    {
        Array.Fill(array, (byte)173);
        return ValueTask.CompletedTask;
    });
    GC.Collect();
    if (arrays.Any(array => array.Any(value => value != 173))) throw new InvalidOperationException("Managed object contents changed across collection.");
}
Console.WriteLine("Runtime allocation smoke test passed.");
