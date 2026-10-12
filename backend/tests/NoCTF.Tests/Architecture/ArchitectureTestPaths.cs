namespace NoCTF.Tests.Architecture;

internal static class ArchitectureTestPaths
{
    internal static string BackendRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NoCTF.slnx")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("The architecture tests require a NoCTF source checkout.");
    }
}
