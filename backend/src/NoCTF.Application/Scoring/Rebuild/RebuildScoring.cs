using NoCTF.Application.Scoring.Evaluation;
using NoCTF.Application.Scoring.Ports;

namespace NoCTF.Application.Scoring.Rebuild;

/// <summary>Rebuilds a disposable scoring stream and activates it only after catching the permanent stream.</summary>
public sealed class RebuildScoring(IScoringRebuildStore store, IScoringRuleEvaluator evaluator)
{
    public async Task ExecuteAsync(Guid competitionId, CancellationToken cancellationToken = default)
    {
        var lease = await store.BeginAsync(competitionId, cancellationToken);
        var context = await store.LoadContextAsync(competitionId, cancellationToken);
        var highWaterMark = lease.SubmissionHighWaterMark;

        while (true)
        {
            var history = await store.ReadSubmissionEventsAsync(
                competitionId,
                0,
                highWaterMark,
                cancellationToken);
            var derived = evaluator.Evaluate(context, history);
            await store.ReplaceStagingEventsAsync(lease, derived, cancellationToken);

            var latest = await store.GetSubmissionHighWaterMarkAsync(competitionId, cancellationToken);
            if (latest == highWaterMark)
            {
                await store.ActivateAsync(lease, context, highWaterMark, cancellationToken);
                return;
            }

            highWaterMark = latest;
            context = await store.LoadContextAsync(competitionId, cancellationToken);
        }
    }
}
