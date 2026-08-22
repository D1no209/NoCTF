namespace NoCTF.Domain.Identity;

public enum UserAccountLifecycleAction : short
{
    Activated,
    Banned,
    Disabled,
    Anonymized,
    PhysicallyDeleted
}
