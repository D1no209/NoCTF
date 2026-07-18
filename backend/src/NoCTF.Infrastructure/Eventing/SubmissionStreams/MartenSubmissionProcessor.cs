using JasperFx.Events;
using Marten;
using Marten.Events;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Processing;
using NoCTF.GameModes.Registration;

namespace NoCTF.Infrastructure.Eventing.SubmissionStreams;

/// <summary>Loads permanent submissions, delegates mode rules, and appends one immutable outcome.</summary>
public sealed class MartenSubmissionProcessor(
    IDocumentSession session,
    SubmissionEvaluationContextLoader contextLoader,
    FixArchiveValidator archiveValidator,
    IFixSubmissionVerifier fixVerifier) : ISubmissionProcessor
{
    public Task ProcessFlagAsync(Guid competitionId, Guid submissionId, CancellationToken cancellationToken) =>
        ProcessAsync(competitionId, submissionId, cancellationToken, EvaluateFlagAsync);

    public Task ProcessFixAsync(Guid competitionId, Guid submissionId, CancellationToken cancellationToken) =>
        ProcessAsync(competitionId, submissionId, cancellationToken, EvaluateFixAsync);

    private async Task ProcessAsync(
        Guid competitionId,
        Guid submissionId,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<IEvent>, Guid, CancellationToken, Task<ISubmissionStreamEvent>> evaluator)
    {
        var receipt = await session.LoadAsync<SubmissionOutcomeReceipt>(submissionId, cancellationToken);
        if (receipt is not null)
            return;

        var events = await session.Events.FetchStreamAsync(StreamIds.Submission(competitionId), token: cancellationToken);
        var existingOutcome = events.FirstOrDefault(item => item.Data switch
        {
            FlagSubmissionEvaluated outcome => outcome.SubmissionId == submissionId,
            FixSubmissionEvaluated outcome => outcome.SubmissionId == submissionId,
            _ => false
        });
        if (existingOutcome is not null)
        {
            session.Insert(new SubmissionOutcomeReceipt
            {
                Id = submissionId,
                CompetitionId = competitionId,
                SubmissionId = submissionId,
                Outcome = existingOutcome.Data switch
                {
                    FlagSubmissionEvaluated flag => flag.Outcome,
                    FixSubmissionEvaluated fix => fix.Outcome,
                    _ => throw new InvalidOperationException("Unexpected submission outcome event.")
                },
                StreamSequence = existingOutcome.Version,
                RecordedAt = existingOutcome.Timestamp
            });
            await session.SaveChangesAsync(cancellationToken);
            return;
        }

        SubmissionProcessingOrder.EnsureEarlierSubmissionsCompleted(events, submissionId);

        var outcome = await evaluator(events, submissionId, cancellationToken);
        var state = await session.Events.FetchStreamStateAsync(StreamIds.Submission(competitionId), cancellationToken)
            ?? throw new InvalidOperationException("Submission stream was not found.");
        session.Events.Append(StreamIds.Submission(competitionId), state.Version, outcome);
        session.Insert(new SubmissionOutcomeReceipt
        {
            Id = submissionId,
            CompetitionId = competitionId,
            SubmissionId = submissionId,
            Outcome = outcome switch
            {
                FlagSubmissionEvaluated flag => flag.Outcome,
                FixSubmissionEvaluated fix => fix.Outcome,
                _ => throw new InvalidOperationException("Unexpected submission outcome event.")
            },
            StreamSequence = state.Version + 1,
            RecordedAt = DateTimeOffset.UtcNow
        });
        await session.SaveChangesAsync(cancellationToken);
    }

    private async Task<ISubmissionStreamEvent> EvaluateFlagAsync(
        IReadOnlyList<IEvent> events,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var received = events.Select(item => item.Data).OfType<FlagSubmissionReceived>()
            .Single(item => item.SubmissionId == submissionId);
        var context = await contextLoader.LoadAsync(events, received.CompetitionId, received.TeamId, received.ChallengeId,
            events.Single(item => item.Data is FlagSubmissionReceived flag && flag.SubmissionId == submissionId).Version,
            received.ReceivedAt,
            cancellationToken);
        var result = GameModeSubmissionCatalog.GetFlag(context.Mode).EvaluateFlag(received, context);
        return new FlagSubmissionEvaluated(
            submissionId,
            received.CompetitionId,
            received.TeamId,
            received.ChallengeId,
            result.Outcome,
            DateTimeOffset.UtcNow,
            result.ErrorCode,
            result.OriginalRound ?? result.Round,
            result.ConsumedAttempt);
    }

    private async Task<ISubmissionStreamEvent> EvaluateFixAsync(
        IReadOnlyList<IEvent> events,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var received = events.Select(item => item.Data).OfType<FixSubmissionReceived>()
            .Single(item => item.SubmissionId == submissionId);
        var context = await contextLoader.LoadAsync(events, received.CompetitionId, received.TeamId, received.ChallengeId,
            events.Single(item => item.Data is FixSubmissionReceived fix && fix.SubmissionId == submissionId).Version,
            received.ReceivedAt,
            cancellationToken);
        var archive = await archiveValidator.ValidateAsync(received.Archive, cancellationToken);
        context = context with { Archive = archive };
        if (archive.Status == ArchiveValidationStatus.Valid)
        {
            var verification = await fixVerifier.VerifyAsync(received, cancellationToken);
            if (verification.Status == FixVerificationStatus.PlatformFailed)
                context = context with { Archive = new FixArchiveEvidence(
                    ArchiveValidationStatus.PlatformFailed,
                    verification.ErrorCode ?? SubmissionErrorCode.ArchiveValidationUnavailable) };
            else if (verification.Status == FixVerificationStatus.TeamFailure)
                context = context with { Archive = new FixArchiveEvidence(
                    ArchiveValidationStatus.TeamFailure,
                    verification.ErrorCode ?? SubmissionErrorCode.FixArchiveHashMismatch) };
        }
        var result = GameModeSubmissionCatalog.GetFix(context.Mode).EvaluateFix(received, context);
        return new FixSubmissionEvaluated(
            submissionId,
            received.CompetitionId,
            received.TeamId,
            received.ChallengeId,
            result.Outcome,
            result.ConsumedAttempt,
            DateTimeOffset.UtcNow,
            result.ErrorCode,
            result.Round);
    }

}
