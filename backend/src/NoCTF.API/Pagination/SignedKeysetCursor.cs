using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace NoCTF.API.Pagination;

public sealed record KeysetPosition(DateTimeOffset CreatedAt, Guid Id);

public sealed class PaginationOptions
{
    public string SigningKey { get; set; } = string.Empty;
}

public sealed class SignedKeysetCursor(IOptions<PaginationOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly byte[] key = Encoding.UTF8.GetBytes(options.Value.SigningKey);

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

    public string EncodeOpaque(string endpoint, string filter, string position)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new OpaqueCursorPayload(endpoint, filter, position),
            JsonOptions);
        var signature = HMACSHA256.HashData(key, payload);
        return $"{WebEncoders.Base64UrlEncode(payload)}.{WebEncoders.Base64UrlEncode(signature)}";
    }

    public bool TryDecodeOpaque(
        string? cursor,
        string endpoint,
        string filter,
        out string? position)
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
            var value = JsonSerializer.Deserialize<OpaqueCursorPayload>(payload, JsonOptions);
            if (value is null
                || string.IsNullOrWhiteSpace(value.Position)
                || !string.Equals(value.Endpoint, endpoint, StringComparison.Ordinal)
                || !string.Equals(value.Filter, filter, StringComparison.Ordinal))
                return false;
            position = value.Position;
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException)
        {
            return false;
        }
    }

    private sealed record CursorPayload(
        string Endpoint,
        string Filter,
        DateTimeOffset CreatedAt,
        Guid Id);

    private sealed record OpaqueCursorPayload(
        string Endpoint,
        string Filter,
        string Position);
}
