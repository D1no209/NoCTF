using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Auth;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Auth;

public class RefreshTokenEndpoint(ApplicationDbContext dbContext, JwtTokenService jwtService) : Endpoint<EmptyRequest, LoginResponse>
{
    public override void Configure()
    {
        Post("/api/auth/refresh");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
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
            AccessToken = jwtService.GenerateToken(user),
            UserName = user.UserName,
            Role = user.Role.ToString().ToLowerInvariant()
        }, cancellation: ct);
    }
}
