using System.Diagnostics;
using System.Text.Json;
using NATS.Client.Core;
using NoCTF.Application.Notifications;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Infrastructure.Notifications;

public sealed class NatsLeaderboardRefreshPublisher(INatsConnection connection)
    : ILeaderboardRefreshPublisher
{
    public const string Subject = "noctf.v3.ui.leaderboard-refreshes";

    public async Task PublishAsync(
        ScoreboardProjection projection,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            await connection.PublishAsync(Subject,
                data: JsonSerializer.SerializeToUtf8Bytes(ScoreboardUpdated.From(projection),
                    NoCtfMessageJsonContext.Default.ScoreboardUpdated),
                cancellationToken: cancellationToken);
        }
        catch
        {
            outcome = "failure";
            throw;
        }
        finally
        {
            NoCtfTelemetry.RecordNatsOperation(
                "leaderboard_publish", outcome,
                Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }
}
