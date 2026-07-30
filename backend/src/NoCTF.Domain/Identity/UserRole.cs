namespace NoCTF.Domain.Identity;

/// <summary>Defines a user's platform-wide authorization role.</summary>
public enum UserRole
{
    User,
    Organizer,
    Administrator
}

public static class UserRolePolicy
{
    public static bool CanManageResources(this UserRole role) =>
        role is UserRole.Organizer or UserRole.Administrator;
}
