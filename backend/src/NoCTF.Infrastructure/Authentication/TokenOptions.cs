namespace NoCTF.Infrastructure.Authentication;

public sealed class AuthenticationTokenOptions
{
    public const string SectionName = "Authentication";

    public string Issuer { get; init; } = "NoCTF";

    public string Audience { get; init; } = "NoCTF.Api";

    public string RefreshAudience { get; init; } = "NoCTF.Refresh";

    public string SigningKey { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 15;
}

public sealed class RunnerScoringOptions
{
    public const string SectionName = "RunnerScoring";

    public string Issuer { get; init; } = "NoCTF.Runner";

    public string Audience { get; init; } = "NoCTF.ScoringInput";

    public string SigningKey { get; init; } = string.Empty;

    public Uri? CallbackBaseUrl { get; init; }
}

public sealed class SeedAdministratorOptions
{
    public const string SectionName = "SeedAdmin";

    public string UserName { get; init; } = "admin";

    public string Email { get; init; } = "admin@noctf.local";

    public string? Password { get; init; }
}

public sealed class EmailVerificationProtectionOptions
{
    public const string SectionName = "EmailVerification";

    public string? EncryptionKey { get; init; }
}
