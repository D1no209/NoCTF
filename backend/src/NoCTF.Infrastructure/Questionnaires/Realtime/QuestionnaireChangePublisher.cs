using System.Text.Json;
using NATS.Client.Core;
using NoCTF.Application.Messaging;
using NoCTF.Application.Questionnaires.Realtime;

namespace NoCTF.Infrastructure.Questionnaires.Realtime;

public sealed class NatsQuestionnaireChangePublisher(INatsConnection connection) : IQuestionnaireChangePublisher
{
    public const string Subject = "noctf.v3.ui.questionnaires";
    public async Task PublishAsync(QuestionnaireChanged change, CancellationToken ct) => await connection.PublishAsync(Subject,
        JsonSerializer.SerializeToUtf8Bytes(change, NoCtfMessageJsonContext.Default.QuestionnaireChanged), cancellationToken: ct);
}
public sealed class NoOpQuestionnaireChangePublisher : IQuestionnaireChangePublisher
{
    public Task PublishAsync(QuestionnaireChanged change, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
}
