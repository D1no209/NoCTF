using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using NoCTF.API.Auth;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Auth;

public class RefreshTokenEndpoint(ApplicationDbContext dbContext, JwtTokenService jwtService) : Endpoint<EmptyRequest, LoginResponse>
{
    public override void Configure()
    {
        Post("/api/auth/refresh");
        Options(builder => builder.RequireRateLimiting("auth-refresh"));
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var sessionStartedAtClaim = User.FindFirst(JwtTokenService.SessionStartedAtClaim)?.Value;
        if (!long.TryParse(
                sessionStartedAtClaim,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var sessionStartedAtSeconds))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }
        DateTimeOffset sessionStartedAt;
        try
        {
            sessionStartedAt = DateTimeOffset.FromUnixTimeSeconds(sessionStartedAtSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }
        if (!jwtService.IsSessionActive(sessionStartedAt))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var user = await dbContext.Users.FindAsync(new object[] { userId }, ct);
        if (user is null)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        await SendAsync(new LoginResponse
        {
            AccessToken = jwtService.GenerateToken(user, sessionStartedAt),
            UserName = user.UserName,
            Role = user.Role.ToString()
        }, cancellation: ct);
    }
}
