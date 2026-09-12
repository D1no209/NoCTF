using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Pagination;

namespace NoCTF.API.Endpoints.Teams.WriteUps;

public sealed record TeamWriteUpPreviewGrant(
    string Token,
    string PreviewUrl,
    DateTimeOffset ExpiresAt);

public sealed class TeamWriteUpPreviewTicketCodec(
    IOptions<PaginationOptions> signingOptions,
    IOptions<RefreshHttpOptions> httpOptions,
    TimeProvider timeProvider)
{
    private const string SecureCookieName = "__Secure-noctf_writeup_preview";
    private const string InsecureCookieName = "noctf_writeup_preview";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static ReadOnlySpan<byte> Purpose => "NoCTF.TeamWriteUpPreview.v1"u8;

    private readonly byte[] key = HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(signingOptions.Value.SigningKey),
        Purpose);

    public string CookieName => httpOptions.Value.RefreshCookieSecure
        ? SecureCookieName
        : InsecureCookieName;

    public TeamWriteUpPreviewGrant Issue(
        Guid competitionId,
        Guid teamId,
        Guid fileId)
    {
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(
            timeProvider.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds());
        var payload = Encoding.UTF8.GetBytes(string.Join(
            ':',
            competitionId.ToString("N"),
            teamId.ToString("N"),
            fileId.ToString("N"),
            expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        var signature = HMACSHA256.HashData(key, payload);
        var token = $"{WebEncoders.Base64UrlEncode(payload)}.{WebEncoders.Base64UrlEncode(signature)}";
        return new(token, PreviewPath(competitionId, teamId), expiresAt);
    }

    public bool TryValidate(
        string? token,
        Guid competitionId,
        Guid teamId,
        out Guid fileId)
    {
        fileId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token))
            return false;
        var tokenParts = token.Split('.', 2);
        if (tokenParts.Length != 2)
            return false;
        try
        {
            var payload = WebEncoders.Base64UrlDecode(tokenParts[0]);
            var suppliedSignature = WebEncoders.Base64UrlDecode(tokenParts[1]);
            var expectedSignature = HMACSHA256.HashData(key, payload);
            if (!CryptographicOperations.FixedTimeEquals(
                    suppliedSignature,
                    expectedSignature))
            {
                return false;
            }

            var values = Encoding.UTF8.GetString(payload).Split(':', 4);
            if (values.Length != 4
                || !Guid.TryParseExact(values[0], "N", out var ticketCompetitionId)
                || !Guid.TryParseExact(values[1], "N", out var ticketTeamId)
                || !Guid.TryParseExact(values[2], "N", out var ticketFileId)
                || !long.TryParse(
                    values[3],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var expiresAtUnixSeconds)
                || ticketCompetitionId != competitionId
                || ticketTeamId != teamId
                || timeProvider.GetUtcNow()
                    >= DateTimeOffset.FromUnixTimeSeconds(expiresAtUnixSeconds))
            {
                return false;
            }

            fileId = ticketFileId;
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public CookieOptions CookieOptions(Guid competitionId, Guid teamId) => new()
    {
        HttpOnly = true,
        Secure = httpOptions.Value.RefreshCookieSecure,
        SameSite = SameSiteMode.Strict,
        Path = PreviewPath(competitionId, teamId),
        MaxAge = Lifetime,
        IsEssential = true
    };

    public static string PreviewPath(Guid competitionId, Guid teamId) =>
        $"/api/v1/writeup-previews/{competitionId:N}/{teamId:N}";
}
