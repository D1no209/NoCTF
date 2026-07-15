namespace NoCTF.Infrastructure.Storage;

/// <summary>
/// Defines the object-key contract shared by every storage backend. Keys are
/// relative, canonical paths; URL-encoded or platform-specific path tricks are
/// rejected before they reach either the local filesystem or an object store.
/// </summary>
internal static class StorageObjectKey
{
    private const int MaxDecodePasses = 8;
    private const int MaxKeyLength = 1024;
    private const int MaxSegmentLength = 255;
    private static readonly char[] WindowsInvalidNameChars = ['<', '>', ':', '"', '|', '?', '*'];

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Invalid();

        var normalized = value.Replace('\\', '/');
        for (var pass = 0; pass < MaxDecodePasses; pass++)
        {
            ValidatePercentEncoding(normalized);

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(normalized).Replace('\\', '/');
            }
            catch (UriFormatException)
            {
                throw Invalid();
            }

            if (string.Equals(decoded, normalized, StringComparison.Ordinal))
                break;

            normalized = decoded;
            if (pass == MaxDecodePasses - 1)
                throw Invalid();
        }

        if (normalized.Length == 0 ||
            normalized.Length > MaxKeyLength ||
            normalized[0] == '/' ||
            normalized[^1] == '/' ||
            Path.IsPathRooted(normalized) ||
            Path.IsPathFullyQualified(normalized) ||
            normalized.Contains("//", StringComparison.Ordinal) ||
            normalized.Contains('?') ||
            normalized.Contains('#') ||
            normalized.Any(char.IsControl))
        {
            throw Invalid();
        }

        var segments = normalized.Split('/');
        if (segments.Any(segment =>
                segment.Length == 0 ||
                segment.Length > MaxSegmentLength ||
                segment is "." or ".." ||
                segment.EndsWith(' ') ||
                segment.EndsWith('.') ||
                segment.IndexOfAny(WindowsInvalidNameChars) >= 0 ||
                IsWindowsDeviceName(segment)))
        {
            throw Invalid();
        }

        // Drive-qualified paths are not considered rooted on every platform,
        // so reject them explicitly to keep Windows and Linux behavior equal.
        if (segments[0].Length >= 2 && char.IsAsciiLetter(segments[0][0]) && segments[0][1] == ':')
            throw Invalid();

        return normalized;
    }

    private static bool IsWindowsDeviceName(string segment)
    {
        var stem = segment.Split('.', 2)[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return stem.Length == 4 &&
               (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
               stem[3] is >= '1' and <= '9';
    }

    private static void ValidatePercentEncoding(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
                continue;

            if (index + 2 >= value.Length ||
                !Uri.IsHexDigit(value[index + 1]) ||
                !Uri.IsHexDigit(value[index + 2]))
            {
                throw Invalid();
            }

            index += 2;
        }
    }

    private static InvalidOperationException Invalid()
        => new("Storage key is invalid.");
}
