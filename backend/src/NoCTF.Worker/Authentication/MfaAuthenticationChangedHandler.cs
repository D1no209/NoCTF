using System.Text.Json;
using NATS.Client.Core;
using NoCTF.Application.Messaging;

namespace NoCTF.Worker.Authentication;

public sealed class MfaAuthenticationChangedHandler(INatsConnection connection)
{
    public const string Subject = "noctf.auth.mfa.changed.v1";
    public ValueTask Handle(MfaAuthenticationChanged message, CancellationToken ct) =>
        connection.PublishAsync(Subject, JsonSerializer.SerializeToUtf8Bytes(message, NoCtfMessageJsonContext.Default.MfaAuthenticationChanged), cancellationToken: ct);
}
