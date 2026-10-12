using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using NoCTF.Hosting.ManagedRuntime;

namespace NoCTF.Benchmarks;

internal static class Program
{
    private static void Main(string[] args)
    {
        var identity = ManagedRuntimeIdentity.Read(typeof(Program).Assembly);
        identity.Validate();
        Console.WriteLine(identity.ToJson());
        if (args.Contains("--runtime-info", StringComparer.Ordinal)) return;
        // One externally isolated benchmark process per runtime variant. A normal BDN
        // child process could otherwise silently use the machine's official runtime.
        var config = ManualConfig.Create(DefaultConfig.Instance).AddJob(Job.Default
            .WithToolchain(InProcessNoEmitToolchain.Instance).WithId(identity.ActualFlavor.ToString()));
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
