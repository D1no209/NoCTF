namespace NoCTF.Domain.Identity;

public enum UserAccountLifecycleAction : short
{
    Banned,
    Disabled,
    Anonymized,
    PhysicallyDeleted
}
