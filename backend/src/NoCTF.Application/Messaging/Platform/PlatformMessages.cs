namespace NoCTF.Application.Messaging;

public sealed record SendEmailVerification(Guid UserId, string Token);
