using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NoCTF.Application.Authentication;

namespace NoCTF.Infrastructure.Authentication;

public sealed class RunnerScoringTokenIssuer(IConfiguration configuration)
    : IRunnerScoringTokenIssuer
{
    private readonly string issuer = configuration["RunnerScoring:Issuer"] ?? "NoCTF.Runner";
    private readonly string audience = configuration["RunnerScoring:Audience"] ?? "NoCTF.ScoringInput";
    private readonly byte[] key = ReadKey(configuration["RunnerScoring:SigningKey"]);

    public string Issue(string runnerId, DateTimeOffset now) =>
        Write(runnerId, now, now.AddMinutes(5), [new("permission", "runner:callback")]);

    public string IssueFixArchiveRead(
        string runnerId,
        Guid uploadId,
        Guid submissionId,
        DateTimeOffset now) =>
        Write(runnerId, now, now.AddMinutes(5),
        [
            new("permission", "awdp:fix-archive:read"),
            new("patch_upload_id", uploadId.ToString("D")),
            new("submission_id", submissionId.ToString("D"))
        ]);

    public string IssueAwdChecker(AwdCheckerTokenRequest request) =>
        Write(request.RunnerId, request.IssuedAt, request.Deadline.AddHours(24),
        [
            new("permission", "awd:check-result:write"),
            new("resource", $"runtime:{request.RuntimeInstanceId:D}"),
            new("runtime_instance_id", request.RuntimeInstanceId.ToString("D")),
            new("generation", request.Generation.ToString(), ClaimValueTypes.Integer32),
            new("checker_sequence", request.CheckerSequence.ToString(), ClaimValueTypes.Integer64),
            new("processing_version", request.ProcessingVersion.ToString(), ClaimValueTypes.Integer64),
            new("deadline", request.Deadline.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        ]);

    private string Write(
        string runnerId,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        IReadOnlyList<Claim> claims)
    {
        var allClaims = new List<Claim>(claims.Count + 4)
        {
            new(JwtRegisteredClaimNames.Sub, runnerId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, issuedAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("token_type", "internal")
        };
        allClaims.AddRange(claims);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(key),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            allClaims,
            issuedAt.UtcDateTime,
            expiresAt.UtcDateTime,
            credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static byte[] ReadKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) < 32)
            throw new InvalidOperationException(
                "RunnerScoring:SigningKey must contain at least 32 UTF-8 bytes.");
        return Encoding.UTF8.GetBytes(value);
    }
}
