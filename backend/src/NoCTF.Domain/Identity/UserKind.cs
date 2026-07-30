namespace NoCTF.Domain.Identity;

/// <summary>Distinguishes interactive people from non-interactive automation identities.</summary>
public enum UserKind : short
{
    Human = 0,
    Bot = 1
}
