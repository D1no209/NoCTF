namespace NoCTF.Domain.Shared;

/// <summary>Stores the archival state shared by soft-deletable entities.</summary>
public sealed class SoftDeleteState
{
    /// <summary>Gets or sets whether the entity is deleted.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>Gets or sets when the entity was deleted.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Gets or sets who deleted the entity.</summary>
    public Guid? DeletedById { get; set; }
}
