using System.Text.Json;
using NATS.Client.Core;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;

namespace NoCTF.Infrastructure.Competitions.Events;

public interface ICompetitionEventRefreshPublisher
{
    Task PublishAsync(CompetitionEventCommitted message, CancellationToken cancellationToken);
}

public sealed class NatsCompetitionEventRefreshPublisher(INatsConnection connection, IServiceScopeFactory scopes)
    : ICompetitionEventRefreshPublisher
{
    public const string Subject = "noctf.v3.ui.competition-events";

    public async Task PublishAsync(
        CompetitionEventCommitted message, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (!await scope.ServiceProvider.GetRequiredService<ICompetitionEventApplicabilityReader>()
            .CanPublishAsync(message.CompetitionId, message.Kind, cancellationToken)) return;
        await connection.PublishAsync(Subject,
            data: JsonSerializer.SerializeToUtf8Bytes(message,
                NoCtfMessageJsonContext.Default.CompetitionEventCommitted),
            cancellationToken: cancellationToken);
    }
}

public sealed class NoOpCompetitionEventRefreshPublisher
    : ICompetitionEventRefreshPublisher
{
    public Task PublishAsync(
        CompetitionEventCommitted message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
