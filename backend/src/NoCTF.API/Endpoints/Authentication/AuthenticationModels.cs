namespace NoCTF.API.Endpoints.Authentication;

public sealed class LoginRequest
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed record LoginResponse(
    Guid UserId,
    string UserName,
    string Role,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt);
