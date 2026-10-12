using NoCTF.Hosting.ManagedRuntime;

namespace NoCTF.Tests;

public static class ManagedRuntimeSession
{
    [Before(TestSession)]
    public static void VerifyTestProcessRuntime()
    {
        var identity = ManagedRuntimeIdentity.Read(typeof(ManagedRuntimeSession).Assembly);
        identity.Validate();
        Console.WriteLine("NOCTF_TEST_RUNTIME=" + identity.ToJson());
        if (Environment.GetEnvironmentVariable("NOCTF_TEST_RUNTIME_RECEIPT") is { Length: > 0 } receipt)
        {
            using var file = new FileStream(receipt, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(file);
            writer.Write(identity.ToJson());
        }
    }
}
