using System.Text.Json;
using Marten;
using Marten.Events;
using JasperFx.Events;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Persistence;
using NoCTF.GameModes.Awdp.Configuration;
using EF = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions;

namespace NoCTF.Infrastructure.Eventing.SubmissionStreams;

/// <summary>Resolves queued submissions from the permanent stream and appends exactly one outcome.</summary>
public sealed class MartenSubmissionProcessor(
    IDocumentSession session,
    NoCtfDbContext db) : ISubmissionProcessor
{
    public Task ProcessFlagAsync(Guid competitionId, Guid submissionId, CancellationToken cancellationToken) =>
        ProcessAsync(competitionId, submissionId, cancellationToken, ProcessFlag);

    public Task ProcessFixAsync(Guid competitionId, Guid submissionId, CancellationToken cancellationToken) =>
        ProcessAsync(competitionId, submissionId, cancellationToken, ProcessFix);

    private async Task ProcessAsync(
        Guid competitionId,
        Guid submissionId,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<IEvent>, Guid, CancellationToken, Task<object>> resolver)
    {
        var events = await session.Events.FetchStreamAsync(StreamIds.Submission(competitionId), token: cancellationToken);
        var existing = events.Select(item => item.Data).FirstOrDefault(item =>
            item switch
            {
                FlagSubmissionEvaluated evaluated => evaluated.SubmissionId == submissionId,
                FixSubmissionEvaluated evaluated => evaluated.SubmissionId == submissionId,
                _ => false
            });
        if (existing is not null) return;

        var outcome = await resolver(events, submissionId, cancellationToken);
        var state = await session.Events.FetchStreamStateAsync(StreamIds.Submission(competitionId), cancellationToken)
            ?? throw new InvalidOperationException("Submission stream was not found.");
        session.Events.Append(StreamIds.Submission(competitionId), state.Version, outcome);
        await session.SaveChangesAsync(cancellationToken);
    }

    private async Task<object> ProcessFlag(
        IReadOnlyList<IEvent> events,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var received = events.Select(item => item.Data).OfType<FlagSubmissionReceived>()
            .Single(item => item.SubmissionId == submissionId);
        var challenge = await EF.SingleAsync(db.Challenges.AsNoTracking(),
            item => item.Id == received.ChallengeId, cancellationToken);
        var configuration = await EF.SingleOrDefaultAsync(db.ChallengeConfigurations.AsNoTracking(),
            item => item.ChallengeId == received.ChallengeId, cancellationToken);
        var expectedFlag = ReadFlag(configuration?.Json);
        var priorCorrect = events.Select(item => item.Data).OfType<FlagSubmissionEvaluated>()
            .Any(item => item.ChallengeId == received.ChallengeId && item.TeamId == received.TeamId
                && item.Outcome == SubmissionOutcome.Correct);
        var crossTeam = events.Select(item => item.Data).OfType<FlagSubmissionEvaluated>()
            .Any(item => item.ChallengeId == received.ChallengeId && item.Outcome == SubmissionOutcome.Correct
                && item.TeamId != received.TeamId);
        var outcome = crossTeam
            ? SubmissionOutcome.CrossTeam
            : priorCorrect
                ? SubmissionOutcome.Duplicate
                : string.Equals(received.Flag, expectedFlag, StringComparison.Ordinal)
                    ? SubmissionOutcome.Correct
                    : SubmissionOutcome.Wrong;
        return new FlagSubmissionEvaluated(
            submissionId,
            received.CompetitionId,
            received.TeamId,
            challenge.Id,
            outcome,
            DateTimeOffset.UtcNow);
    }

    private async Task<object> ProcessFix(
        IReadOnlyList<IEvent> events,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var received = events.Select(item => item.Data).OfType<FixSubmissionReceived>()
            .Single(item => item.SubmissionId == submissionId);
        var configuration = await EF.SingleOrDefaultAsync(db.ChallengeConfigurations.AsNoTracking(),
            item => item.ChallengeId == received.ChallengeId, cancellationToken);
        var maxAttempts = ReadMaxFixAttempts(configuration?.Json);
        var priorFailures = events.Select(item => item.Data).OfType<FixSubmissionEvaluated>()
            .Count(item => item.TeamId == received.TeamId && item.ChallengeId == received.ChallengeId
                && item.Outcome is SubmissionOutcome.Wrong or SubmissionOutcome.PlatformFailed);
        var valid = received.Archive.Length > 0 && !string.IsNullOrWhiteSpace(received.Archive.ObjectKey);
        var exhausted = priorFailures >= maxAttempts;
        var outcome = exhausted
            ? SubmissionOutcome.AttemptsExhausted
            : valid ? SubmissionOutcome.Correct : SubmissionOutcome.Wrong;
        return new FixSubmissionEvaluated(
            submissionId,
            received.CompetitionId,
            received.TeamId,
            received.ChallengeId,
            outcome,
            outcome == SubmissionOutcome.Wrong,
            DateTimeOffset.UtcNow);
    }

    private static string? ReadFlag(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("flag", out var flag) ? flag.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static int ReadMaxFixAttempts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 3;
        try
        {
            var config = JsonSerializer.Deserialize<AwdpChallengeConfiguration>(json);
            return config?.MaxFixAttempts > 0 ? config.MaxFixAttempts : 3;
        }
        catch (JsonException) { return 3; }
    }
}
