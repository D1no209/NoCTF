using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NoCTF.Modeling.Generators;

namespace NoCTF.Tests.Unit.Modeling;

public sealed class PersistentDiscriminatorGeneratorTests
{
    [Test]
    public async Task Reports_missing_discriminator_and_unsealed_leaf()
    {
        var diagnostics = Run("""
            using System;
            namespace NoCTF.Domain.Shared
            {
                [AttributeUsage(AttributeTargets.Class)] public sealed class PersistentHierarchyAttribute : Attribute { }
                [AttributeUsage(AttributeTargets.Class)] public sealed class PersistentDiscriminatorAttribute(string value) : Attribute { }
            }
            namespace Example
            {
                [NoCTF.Domain.Shared.PersistentHierarchy] public abstract class Root { }
                public sealed class Missing : Root { }
                [NoCTF.Domain.Shared.PersistentDiscriminator("open")] public class Open : Root { }
            }
            """);

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Id))
            .Contains("NCTF001");
        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Id))
            .Contains("NCTF002");
    }

    [Test]
    public async Task Reports_duplicate_discriminators()
    {
        var diagnostics = Run("""
            using System;
            namespace NoCTF.Domain.Shared
            {
                [AttributeUsage(AttributeTargets.Class)] public sealed class PersistentHierarchyAttribute : Attribute { }
                [AttributeUsage(AttributeTargets.Class)] public sealed class PersistentDiscriminatorAttribute(string value) : Attribute { }
            }
            namespace Example
            {
                [NoCTF.Domain.Shared.PersistentHierarchy] public abstract class Root { }
                [NoCTF.Domain.Shared.PersistentDiscriminator("same")] public sealed class First : Root { }
                [NoCTF.Domain.Shared.PersistentDiscriminator("same")] public sealed class Second : Root { }
            }
            """);

        await Assert.That(diagnostics.Count(diagnostic => diagnostic.Id == "NCTF003"))
            .IsEqualTo(2);
    }

    private static Diagnostic[] Run(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new PersistentDiscriminatorGenerator().AsSourceGenerator());
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult().Diagnostics.ToArray();
    }
}
