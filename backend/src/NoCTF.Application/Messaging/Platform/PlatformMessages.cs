namespace NoCTF.Application.Messaging;

public sealed record SendEmailVerification(Guid UserId, string Token)
{
    public override string ToString() =>
        $"{nameof(SendEmailVerification)} {{ UserId = {UserId}, Token = [REDACTED] }}";
}

public sealed record SendPasswordReset(Guid UserId, string Token)
{
    public override string ToString() =>
        $"{nameof(SendPasswordReset)} {{ UserId = {UserId}, Token = [REDACTED] }}";
}

public sealed record SendPasswordChangedNotification(Guid UserId);
