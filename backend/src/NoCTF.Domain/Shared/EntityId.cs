namespace NoCTF.Domain.Shared;

/// <summary>Strongly identifies an entity without coupling the domain to a persistence technology.</summary>
public readonly record struct EntityId(Guid Value)
{
    /// <summary>Creates a new identifier.</summary>
    public static EntityId New() => new(Guid.NewGuid());

    /// <summary>Converts the identifier to its underlying value.</summary>
    public static implicit operator Guid(EntityId id) => id.Value;
}
