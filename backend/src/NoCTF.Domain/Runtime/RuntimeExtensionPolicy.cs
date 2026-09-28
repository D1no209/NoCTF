namespace NoCTF.Domain.Runtime;

public static class RuntimeExtensionPolicy
{
    public static DateTimeOffset? CalculateExpiry(
        DateTimeOffset? currentExpiry,
        DateTimeOffset now,
        TimeSpan? extension)
    {
        if (currentExpiry is not { } expiry || expiry <= now
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
