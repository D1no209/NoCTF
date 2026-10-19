using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Worker;

public sealed class StopSolvedChallengeRuntimesHandler(
    NoCtfDbContext db,
    IPostCommitMessagePublisher messages,
    ICompetitionEventRecorder events,
    TimeProvider clock)
{
    public async Task Handle(StopSolvedChallengeRuntimes message, CancellationToken ct)
    {
        var fact = await db.GameplayFacts.AsNoTracking()
            .Where(item => item.Id == message.GameplayFactId
                && item.ConcurrencyStamp == message.ExpectedConcurrencyStamp
                && item.State == GameplayFactState.Completed)
            .Select(item => new
            {
                item.CompetitionId,
                item.CompetitionChallengeId,
                item.TeamId,
                item.Kind,
                item.Result,
                item.TimeEligibility,
                item.OccurredAt
            })
            .SingleOrDefaultAsync(ct);
        if (fact?.CompetitionChallengeId is not Guid challengeId
            || fact.TeamId is not Guid teamId
            || !GameplayFactCompletion.IsSuccessful(fact.Result, fact.TimeEligibility))
            return;

        var mode = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == fact.CompetitionId)
            .Select(item => item.Mode)
            .SingleAsync(ct);
        var purpose = (mode, fact.Kind) switch
        {
            (GameMode.Ctf, GameplayFactKind.FlagAttempt) => RuntimePurpose.Player,
            (GameMode.Awdp, GameplayFactKind.BreakAttempt) => RuntimePurpose.AwdpAttack,
            _ => (RuntimePurpose?)null
        };
        if (purpose is null) return;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var runtimes = await db.RuntimeInstances.IgnoreAutoIncludes().AsSplitQuery()
            .Where(item => item.CompetitionId == fact.CompetitionId
                && item.CompetitionChallengeId == challengeId
                && item.TeamId == teamId
                && item.Purpose == purpose.Value
                && item.CreatedAt <= fact.OccurredAt
                && item.RuntimeKind == RuntimeKind.Container
                && (item.State == RuntimeState.Queued
                    || item.State == RuntimeState.Provisioning
                    || item.State == RuntimeState.Running
                    || item.State == RuntimeState.Failed && item.ProviderReceipt != null))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToArrayAsync(ct);
        var now = clock.GetUtcNow();
        foreach (var runtime in runtimes)
        {
            if (runtime.State == RuntimeState.Queued)
            {
                runtime.State = RuntimeState.Stopped;
                runtime.StoppedAt = now;
            }
            else
            {
                runtime.State = RuntimeState.Stopping;
                await messages.PublishAsync(new StopRuntime(runtime.Id));
            }
            await events.RecordAsync(new(fact.CompetitionId, CompetitionEventKind.RuntimeStateChanged,
                CompetitionEventLevel.Information, CompetitionEventVisibility.Team, now,
                TeamId: teamId, CompetitionChallengeId: challengeId, RuntimeInstanceId: runtime.Id,
                GameplayFactId: message.GameplayFactId, RuntimeState: runtime.State), ct);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await transaction.DisposeAsync();
        await messages.FlushCommittedMessagesAsync();
    }
}
