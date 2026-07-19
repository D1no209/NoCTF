using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NoCTF.Application.Authentication;

namespace NoCTF.Runner.Composition;

public sealed class RunnerScoringTokenIssuer(IConfiguration configuration) : IRunnerScoringTokenIssuer
{
    public string Issue(string runnerId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(runnerId)) throw new ArgumentException("Runner id is required.", nameof(runnerId));
        var keyText = configuration["RunnerScoring:SigningKey"];
        if (string.IsNullOrWhiteSpace(keyText) || Encoding.UTF8.GetByteCount(keyText) < 32)
            throw new InvalidOperationException("RunnerScoring:SigningKey must contain at least 32 UTF-8 bytes.");
        var issuer = configuration["RunnerScoring:Issuer"] ?? "NoCTF.Runner";
        var audience = configuration["RunnerScoring:Audience"] ?? "NoCTF.ScoringInput";
        var lifetime = Math.Clamp(configuration.GetValue("RunnerScoring:LifetimeSeconds", 300), 30, 300);
        var issuedAt = now.UtcDateTime;
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [
                new(JwtRegisteredClaimNames.Sub, runnerId),
                new("runner_id", runnerId),
                new("scope", "scoring.write"),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            ],
            issuedAt,
            issuedAt.AddSeconds(lifetime),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyText)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
