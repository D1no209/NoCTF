namespace NoCTF.Application.Storage;

public sealed record FileUploadLimits(
    long MaximumAvatarBytes,
    long MaximumLogoBytes,
    long MaximumPosterBytes,
    long MaximumAttachmentBytes)
{
    public const long DefaultImageBytes = 12L * 1024 * 1024;
    public const long DefaultAttachmentBytes = 1024L * 1024 * 1024;
    public const long MaximumConfigurableBytes = 1024L * 1024 * 1024;
    public const long MultipartOverheadBytes = 64L * 1024;

    public static FileUploadLimits Default { get; } = new(
        DefaultImageBytes,
        DefaultImageBytes,
        DefaultImageBytes,
        DefaultAttachmentBytes);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        ValidateValue(nameof(MaximumAvatarBytes), MaximumAvatarBytes, errors);
        ValidateValue(nameof(MaximumLogoBytes), MaximumLogoBytes, errors);
        ValidateValue(nameof(MaximumPosterBytes), MaximumPosterBytes, errors);
        ValidateValue(nameof(MaximumAttachmentBytes), MaximumAttachmentBytes, errors);
        return errors;
    }

    public static long MaximumRequestBytes(long maximumFileBytes) =>
        checked(maximumFileBytes + MultipartOverheadBytes);

    private static void ValidateValue(string name, long value, List<string> errors)
    {
        if (value is <= 0 or > MaximumConfigurableBytes)
            errors.Add($"{name} must be between 1 and {MaximumConfigurableBytes} bytes.");
    }
}

public enum FileUploadFailureCode
{
    UploadTooLarge
}
