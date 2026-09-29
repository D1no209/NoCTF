using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Admission;
using NoCTF.Application.Observability;
using NoCTF.Domain.Platform;

namespace NoCTF.Hosting.Observability;

/// <summary>Samples Cap's own daily aggregates without exposing its credentials or site key.</summary>
public sealed class CapTelemetryAgent(
    IServiceScopeFactory scopes,
    ICapTelemetryReader reader,
    TimeProvider clock,
    ILogger<CapTelemetryAgent> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private CapTelemetryReadOutcome? previousFailure;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, clock);
        do
        {
            await PollAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var configuration = await scope.ServiceProvider
                .GetRequiredService<IHumanVerificationConfigurationReader>()
                .GetRuntimeConfigurationAsync(ct);
            var isCap = configuration.Options.Provider == HumanVerificationProvider.Cap;
            NoCtfTelemetry.SetCapTelemetryActive(isCap && configuration.Enabled);
            if (!isCap)
            {
                NoCtfTelemetry.ClearCapTelemetrySnapshot();
                previousFailure = null;
                return;
            }

            var result = await reader.ReadTodayAsync(configuration.Options.Cap, ct);
            NoCtfTelemetry.RecordCapTelemetryPoll(result.Outcome);
            if (result is { Outcome: CapTelemetryReadOutcome.Available,
                Snapshot: { } snapshot })
            {
                NoCtfTelemetry.SetCapTelemetrySnapshot(snapshot, clock.GetUtcNow());
                if (previousFailure is not null)
                    logger.LogInformation("Cap telemetry sampling recovered.");
                previousFailure = null;
                return;
            }

            NoCtfTelemetry.SetCapTelemetryUnavailable();
            if (previousFailure != result.Outcome)
                logger.LogWarning("Cap telemetry sampling unavailable: {Outcome}.",
                    result.Outcome);
            previousFailure = result.Outcome;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutdown must not be reported as a Cap outage.
        }
        catch (Exception exception)
        {
            NoCtfTelemetry.SetCapTelemetryUnavailable();
            NoCtfTelemetry.RecordCapTelemetryPoll(CapTelemetryReadOutcome.Unavailable);
            if (previousFailure != CapTelemetryReadOutcome.Unavailable)
                logger.LogWarning("Cap telemetry sampling failed after {FailureType}.",
                    exception.GetType().Name);
            previousFailure = CapTelemetryReadOutcome.Unavailable;
        }
    }
}
