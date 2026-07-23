using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace NoCTF.API.Pagination;

public sealed record KeysetPosition(DateTimeOffset CreatedAt, Guid Id);

public sealed class SignedKeysetCursor(IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly byte[] key = ReadKey(configuration);

    public string Encode(string endpoint, string filter, KeysetPosition position)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new CursorPayload(endpoint, filter, position.CreatedAt, position.Id),
            JsonOptions);
        var signature = HMACSHA256.HashData(key, payload);
        return $"{WebEncoders.Base64UrlEncode(payload)}.{WebEncoders.Base64UrlEncode(signature)}";
    }

    public bool TryDecode(
        string? cursor,
        string endpoint,
        string filter,
        out KeysetPosition? position)
    {
        position = null;
        if (string.IsNullOrWhiteSpace(cursor))
            return true;
        var parts = cursor.Split('.', 2);
        if (parts.Length != 2)
            return false;
        try
        {
            var payload = WebEncoders.Base64UrlDecode(parts[0]);
            var supplied = WebEncoders.Base64UrlDecode(parts[1]);
            var expected = HMACSHA256.HashData(key, payload);
            if (!CryptographicOperations.FixedTimeEquals(supplied, expected))
                return false;
            var value = JsonSerializer.Deserialize<CursorPayload>(payload, JsonOptions);
            if (value is null
                || !string.Equals(value.Endpoint, endpoint, StringComparison.Ordinal)
                || !string.Equals(value.Filter, filter, StringComparison.Ordinal))
                return false;
            position = new(value.CreatedAt, value.Id);
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException)
        {
            return false;
        }
    }

    private static byte[] ReadKey(IConfiguration configuration)
    {
        var value = configuration["Pagination:SigningKey"]
            ?? configuration["Authentication:SigningKey"];
        if (string.IsNullOrWhiteSpace(value)
            || Encoding.UTF8.GetByteCount(value) < 32)
            throw new InvalidOperationException(
                "Pagination:SigningKey must contain at least 32 UTF-8 bytes.");
        return Encoding.UTF8.GetBytes(value);
    }

    private sealed record CursorPayload(
        string Endpoint,
        string Filter,
        DateTimeOffset CreatedAt,
        Guid Id);
}
