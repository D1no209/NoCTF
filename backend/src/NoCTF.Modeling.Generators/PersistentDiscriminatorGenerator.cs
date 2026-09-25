using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NoCTF.Modeling.Generators;

[Generator]
public sealed class PersistentDiscriminatorGenerator : IIncrementalGenerator
{
    private const string HierarchyAttribute =
        "NoCTF.Domain.Shared.PersistentHierarchyAttribute";
    private const string DiscriminatorAttribute =
        "NoCTF.Domain.Shared.PersistentDiscriminatorAttribute";
    private const string GenerateLeavesAttribute =
        "NoCTF.Domain.Shared.GeneratePersistentLeavesAttribute";

    private static readonly DiagnosticDescriptor MissingDiscriminator = new(
        "NCTF001", "Persistent hierarchy leaf requires a discriminator",
        "Type '{0}' derives from a persistent hierarchy but has no PersistentDiscriminator attribute",
        "Persistence", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor LeafMustBeSealed = new(
        "NCTF002", "Persistent hierarchy leaf must be sealed",
        "Persistent hierarchy leaf '{0}' must be sealed",
        "Persistence", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor DuplicateDiscriminator = new(
        "NCTF003", "Persistent discriminator must be unique",
        "Discriminator '{0}' is duplicated in hierarchy '{1}'",
        "Persistence", DiagnosticSeverity.Error, true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var leaves = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax,
                static (syntaxContext, _) => syntaxContext.SemanticModel.GetDeclaredSymbol(
                    (ClassDeclarationSyntax)syntaxContext.Node) as INamedTypeSymbol)
            .Where(static symbol => symbol is not null)
            .Select(static (symbol, _) => CreateLeaf(symbol!))
            .Where(static leaf => leaf.HasValue)
            .Select(static (leaf, _) => leaf!.Value);
        context.RegisterSourceOutput(leaves.Collect(), Emit);

        var generatedHierarchies = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax,
                static (syntaxContext, _) => CreateHierarchy(
                    syntaxContext.SemanticModel.GetDeclaredSymbol(
                        (ClassDeclarationSyntax)syntaxContext.Node) as INamedTypeSymbol))
            .Where(static hierarchy => hierarchy.HasValue)
            .Select(static (hierarchy, _) => hierarchy!.Value);
        context.RegisterSourceOutput(generatedHierarchies, EmitHierarchy);
    }

    private static Leaf? CreateLeaf(INamedTypeSymbol symbol)
    {
        var hierarchy = FindHierarchy(symbol.BaseType);
        if (hierarchy is null || symbol.IsAbstract)
            return null;
        var attribute = symbol.GetAttributes().FirstOrDefault(candidate =>
            candidate.AttributeClass?.ToDisplayString() == DiscriminatorAttribute);
        var discriminator = attribute?.ConstructorArguments.FirstOrDefault().Value as string;
        return new Leaf(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.Name,
            hierarchy.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            hierarchy.Name,
            discriminator,
            symbol.IsSealed,
            symbol.Locations.FirstOrDefault());
    }

    private static INamedTypeSymbol? FindHierarchy(INamedTypeSymbol? candidate)
    {
        while (candidate is not null)
        {
            if (candidate.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.ToDisplayString() == HierarchyAttribute))
                return candidate;
            candidate = candidate.BaseType;
        }
        return null;
    }

    private static Hierarchy? CreateHierarchy(INamedTypeSymbol? symbol)
    {
        if (symbol is null)
            return null;
        var attribute = symbol.GetAttributes().FirstOrDefault(candidate =>
            candidate.AttributeClass?.ToDisplayString() == GenerateLeavesAttribute);
        if (attribute is null
            || attribute.ConstructorArguments.Length != 2
            || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol enumType
            || attribute.ConstructorArguments[1].Value is not string suffix)
            return null;
        var members = enumType.GetMembers().OfType<IFieldSymbol>()
            .Where(field => field.HasConstantValue)
            .Select(field => new EnumValue(
                field.Name,
                field.ConstantValue?.ToString() ?? "0",
                ToKebabCase(field.Name)))
            .ToImmutableArray();
        return new Hierarchy(
            symbol.ContainingNamespace.ToDisplayString(),
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.Name,
            enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            enumType.Name,
            suffix,
            members);
    }

    private static void EmitHierarchy(SourceProductionContext context, Hierarchy hierarchy)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.Append("namespace ").Append(hierarchy.Namespace).AppendLine();
        source.AppendLine("{");
        source.AppendLine();
        foreach (var member in hierarchy.Members)
        {
            var leafName = member.Name + hierarchy.Suffix;
            source.Append("[global::NoCTF.Domain.Shared.PersistentDiscriminator(\"")
                .Append(member.Discriminator).AppendLine("\")]");
            source.Append("public sealed class ").Append(leafName).Append(" : ")
                .Append(hierarchy.BaseName).AppendLine();
            source.AppendLine("{");
            source.Append("    public ").Append(leafName).Append("() : base(")
                .Append(hierarchy.EnumType).Append('.').Append(member.Name).AppendLine(") { }");
            source.AppendLine("}");
            source.AppendLine();
        }
        source.Append("public static class ").Append(hierarchy.BaseName)
            .AppendLine("GeneratedCatalog");
        source.AppendLine("{");
        source.Append("    public static ").Append(hierarchy.BaseType)
            .Append(" Create(").Append(hierarchy.EnumType).AppendLine(" kind) => kind switch");
        source.AppendLine("    {");
        foreach (var member in hierarchy.Members)
        {
            source.Append("        ").Append(hierarchy.EnumType).Append('.').Append(member.Name)
                .Append(" => new ").Append(member.Name).Append(hierarchy.Suffix).AppendLine("(),");
        }
        source.AppendLine("        _ => throw new global::System.ArgumentOutOfRangeException(nameof(kind), kind, null)");
        source.AppendLine("    };");
        source.AppendLine();
        source.Append("    public static readonly (global::System.Type Leaf, string Value, ")
            .Append(hierarchy.EnumType).AppendLine(" Kind)[] Entries =");
        source.AppendLine("    [");
        foreach (var member in hierarchy.Members)
        {
            source.Append("        (typeof(").Append(member.Name).Append(hierarchy.Suffix)
                .Append("), \"").Append(member.Discriminator).Append("\", ")
                .Append(hierarchy.EnumType).Append('.').Append(member.Name).AppendLine("),");
        }
        source.AppendLine("    ];");
        source.AppendLine();
        source.AppendLine("    public static readonly global::System.Type[] JsonTypes =");
        source.AppendLine("    [");
        foreach (var member in hierarchy.Members)
        {
            source.Append("        typeof(").Append(member.Name).Append(hierarchy.Suffix)
                .AppendLine("),");
        }
        source.AppendLine("    ];");
        source.AppendLine("}");
        source.AppendLine("}");
        context.AddSource(
            hierarchy.BaseName + ".PersistentLeaves.g.cs",
            SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<Leaf> leaves)
    {
        foreach (var leaf in leaves)
        {
            if (string.IsNullOrWhiteSpace(leaf.Discriminator))
                context.ReportDiagnostic(Diagnostic.Create(
                    MissingDiscriminator, leaf.Location, leaf.Name));
            if (!leaf.IsSealed)
                context.ReportDiagnostic(Diagnostic.Create(
                    LeafMustBeSealed, leaf.Location, leaf.Name));
        }

        foreach (var hierarchy in leaves.GroupBy(leaf => leaf.Hierarchy, StringComparer.Ordinal))
        foreach (var duplicate in hierarchy
                     .Where(leaf => !string.IsNullOrWhiteSpace(leaf.Discriminator))
                     .GroupBy(leaf => leaf.Discriminator!, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        foreach (var leaf in duplicate)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuplicateDiscriminator,
                leaf.Location,
                leaf.Discriminator,
                leaf.HierarchyName));
        }

        var valid = leaves
            .Where(leaf => leaf.IsSealed && !string.IsNullOrWhiteSpace(leaf.Discriminator))
            .OrderBy(leaf => leaf.Hierarchy, StringComparer.Ordinal)
            .ThenBy(leaf => leaf.Discriminator, StringComparer.Ordinal)
            .ToArray();
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine("namespace NoCTF.Generated;");
        source.AppendLine();
        source.AppendLine("public static class PersistentDiscriminatorCatalog");
        source.AppendLine("{");
        source.AppendLine("    public static readonly (global::System.Type Hierarchy, global::System.Type Leaf, string Value)[] Entries =");
        source.AppendLine("    [");
        foreach (var leaf in valid)
        {
            source.Append("        (typeof(").Append(leaf.Hierarchy)
                .Append("), typeof(").Append(leaf.Type).Append("), \"")
                .Append(Escape(leaf.Discriminator!)).AppendLine("\"),");
        }
        source.AppendLine("    ];");
        source.AppendLine();
        source.AppendLine("    public static readonly global::System.Type[] JsonTypes =");
        source.AppendLine("    [");
        foreach (var leaf in valid)
            source.Append("        typeof(").Append(leaf.Type).AppendLine("),");
        source.AppendLine("    ];");
        source.AppendLine("}");
        context.AddSource(
            "PersistentDiscriminatorCatalog.g.cs",
            SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string ToKebabCase(string value)
    {
        var output = new StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (index > 0 && char.IsUpper(character)
                && (char.IsLower(value[index - 1])
                    || index + 1 < value.Length && char.IsLower(value[index + 1])))
                output.Append('-');
            output.Append(char.ToLowerInvariant(character));
        }
        return output.ToString();
    }

    private readonly struct Leaf
    {
        public Leaf(string type, string name, string hierarchy, string hierarchyName,
            string? discriminator, bool isSealed, Location? location)
        {
            Type = type;
            Name = name;
            Hierarchy = hierarchy;
            HierarchyName = hierarchyName;
            Discriminator = discriminator;
            IsSealed = isSealed;
            Location = location;
        }

        public string Type { get; }
        public string Name { get; }
        public string Hierarchy { get; }
        public string HierarchyName { get; }
        public string? Discriminator { get; }
        public bool IsSealed { get; }
        public Location? Location { get; }
    }

    private readonly struct EnumValue
    {
        public EnumValue(string name, string value, string discriminator)
        {
            Name = name;
            Value = value;
            Discriminator = discriminator;
        }
        public string Name { get; }
        public string Value { get; }
        public string Discriminator { get; }
    }

    private readonly struct Hierarchy
    {
        public Hierarchy(string @namespace, string baseType, string baseName,
            string enumType, string enumName, string suffix, ImmutableArray<EnumValue> members)
        {
            Namespace = @namespace;
            BaseType = baseType;
            BaseName = baseName;
            EnumType = enumType;
            EnumName = enumName;
            Suffix = suffix;
            Members = members;
        }
        public string Namespace { get; }
        public string BaseType { get; }
        public string BaseName { get; }
        public string EnumType { get; }
        public string EnumName { get; }
        public string Suffix { get; }
        public ImmutableArray<EnumValue> Members { get; }
    }
}
