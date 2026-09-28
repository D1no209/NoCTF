namespace NoCTF.Domain.Shared;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PersistentHierarchyAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PersistentDiscriminatorAttribute(string value) : Attribute
{
    public string Value { get; } = value;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GeneratePersistentLeavesAttribute(Type kindEnum, string suffix) : Attribute
{
    public Type KindEnum { get; } = kindEnum;
    public string Suffix { get; } = suffix;
}
