using System.Text.Json;
using JasperFx.Events;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Infrastructure.Persistence;
using EF = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions;

namespace NoCTF.Infrastructure.Eventing.SubmissionStreams;

public sealed class SubmissionEvaluationContextLoader(
    NoCtfDbContext db,
    IChallengeInstanceFlagReader flagReader)
{
    public async Task<SubmissionEvaluationContext> LoadAsync(
        IReadOnlyList<IEvent> events,
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        long currentSubmissionSequence,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        var competition = await EF.SingleAsync(db.Competitions.AsNoTracking(),
            item => item.Id == competitionId, cancellationToken);
        var competitionConfiguration = await EF.SingleAsync(db.CompetitionConfigurations.AsNoTracking(),
            item => item.CompetitionId == competitionId, cancellationToken);
        var challengeConfiguration = await EF.SingleOrDefaultAsync(db.ChallengeConfigurations.AsNoTracking(),
            item => item.ChallengeId == challengeId, cancellationToken);
        var history = events
            .Select(item => item.Data is ISubmissionStreamEvent submissionEvent
                ? new SubmissionHistoryItem(item.Version, submissionEvent, item.Timestamp)
                : null)
            .OfType<SubmissionHistoryItem>()
            .ToList();
        var awdFlags = events.Select(item => item.Data).OfType<AwdFlagRotated>()
            .Select(item => new AwdFlagEvidence(item.TeamId, item.ChallengeId, item.Flag, item.Round, item.OccurredAt))
            .ToList();

        var expectedFlagAtReceipt = events.Select(item => item.Data)
            .OfType<FlagSubmissionReceived>()
            .SingleOrDefault(item => item.TeamId == teamId
                && item.ChallengeId == challengeId
                && item.ReceivedAt == receivedAt)
            ?.ExpectedFlagAtReceipt;
        var expectedFlag = expectedFlagAtReceipt ?? await flagReader.ReadAsync(
            competitionId, teamId, challengeId, receivedAt, cancellationToken);

        return new(
            competitionId,
            competition.Mode,
            competition.StartTime,
            ReadRoundDuration(competitionConfiguration.Json),
            competitionConfiguration.Json,
            challengeConfiguration?.Json ?? """{"schemaVersion":1}""",
            history,
            currentSubmissionSequence,
            expectedFlag,
            awdFlags,
            null);
    }

    private static int ReadRoundDuration(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 60;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("roundDurationSeconds", out var duration)
                && duration.TryGetInt32(out var value) && value > 0 ? value : 60;
        }
        catch (JsonException)
        {
            return 60;
        }
    }
}
