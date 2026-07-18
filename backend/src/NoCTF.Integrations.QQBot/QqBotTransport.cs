using System.Net.Http.Json;
using NoCTF.Integrations.QQBot.Contracts;

namespace NoCTF.Integrations.QQBot;

public interface IQqBotTransport
{
    Task SendAsync(CompetitionNotificationRequested message, CancellationToken cancellationToken);
}

/// <summary>Delivers QQBot notifications through a configured HTTP adapter; Wolverine owns retries/dead letters.</summary>
public sealed class QqBotTransport(HttpClient client) : IQqBotTransport
{
    public async Task SendAsync(CompetitionNotificationRequested message, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("notifications", message, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
