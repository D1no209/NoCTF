using NoCTF.Hosting.ManagedRuntime;

var identity = ManagedRuntimeIdentity.Read(typeof(Program).Assembly);
identity.Validate();
Console.WriteLine(identity.ToJson());
if (args.Contains("--runtime-info", StringComparer.Ordinal)) return;

// Bounded allocation and cross-thread object identity smoke test, not a performance claim.
for (var pass = 0; pass < 20; pass++)
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
