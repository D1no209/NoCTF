namespace NoCTF.Domain.Runtime;

public static class RuntimeExtensionPolicy
{
    public const int RenewalWindowMinutes = 10;

    public static bool IsWithinRenewalWindow(
        DateTimeOffset? currentExpiry,
        DateTimeOffset now) =>
        currentExpiry is { } expiry
        && expiry > now
        && expiry - now <= TimeSpan.FromMinutes(RenewalWindowMinutes);

    public static DateTimeOffset? CalculateExpiry(
        DateTimeOffset? currentExpiry,
        DateTimeOffset now,
        TimeSpan? extension)
    {
        if (currentExpiry is not { } expiry
            || !IsWithinRenewalWindow(expiry, now)
            || extension is not { } duration || duration <= TimeSpan.Zero)
            return null;

        try
        {
            return expiry.Add(duration);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
