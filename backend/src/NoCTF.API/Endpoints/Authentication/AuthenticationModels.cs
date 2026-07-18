namespace NoCTF.API.Endpoints.Authentication;

public sealed record LoginResponse(
    Guid UserId,
    string UserName,
    string Role,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt);
