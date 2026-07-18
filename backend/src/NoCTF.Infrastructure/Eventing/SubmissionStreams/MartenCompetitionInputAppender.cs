using Marten;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Ports;

namespace NoCTF.Infrastructure.Eventing.SubmissionStreams;

public sealed class MartenCompetitionInputAppender(IDocumentSession session) : ICompetitionInputAppender
{
    public Task<bool> AppendAsync(AwdFlagInput input, CancellationToken cancellationToken) => AppendAsync(
        input.CompetitionId,
        input.IdempotencyKey,
        new AwdFlagRotated(input.CompetitionId, input.TeamId, input.ChallengeId, input.Round, input.Flag, input.OccurredAt, input.IdempotencyKey),
        cancellationToken);

    public Task<bool> AppendAsync(AwdServiceInput input, CancellationToken cancellationToken) => AppendAsync(
        input.CompetitionId,
        input.IdempotencyKey,
        new AwdServiceChecked(input.CompetitionId, input.TeamId, input.ChallengeId, input.Round, input.Observation, input.OccurredAt, input.IdempotencyKey),
        cancellationToken);

    public Task<bool> AppendAsync(KohInput input, CancellationToken cancellationToken) => AppendAsync(
        input.CompetitionId,
        input.IdempotencyKey,
        new KohControlObserved(input.CompetitionId, input.ChallengeId, input.ControllerTeamId, input.ControllerName, input.IsAuthoritative, input.ObservedAt, input.IdempotencyKey),
        cancellationToken);

    public Task<bool> AppendAsync(SystemScoreInput input, CancellationToken cancellationToken) => AppendAsync(
        input.CompetitionId,
        input.IdempotencyKey,
        new SystemScoringInput(input.CompetitionId, input.TeamId, input.Kind, input.Points, input.OccurredAt, input.IdempotencyKey),
        cancellationToken);

    public Task<bool> AppendAsync(PenetrationStageInput input, CancellationToken cancellationToken) => AppendAsync(
        input.CompetitionId,
        input.IdempotencyKey,
        new PenetrationStageCompleted(input.CompetitionId, input.TeamId, input.ChallengeId, input.StageId,
            input.OccurredAt, input.IdempotencyKey),
        cancellationToken);

    private async Task<bool> AppendAsync<T>(Guid competitionId, string idempotencyKey, T @event, CancellationToken cancellationToken)
        where T : ISubmissionStreamEvent
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));

        var streamId = StreamIds.Submission(competitionId);
        var events = await session.Events.FetchStreamAsync(streamId, token: cancellationToken);
        if (events.Any(item => item.Data is SystemScoringInput existing && existing.IdempotencyKey == idempotencyKey)
            || events.Any(item => item.Data is AwdFlagRotated existing && existing.IdempotencyKey == idempotencyKey)
            || events.Any(item => item.Data is AwdServiceChecked existing && existing.IdempotencyKey == idempotencyKey)
            || events.Any(item => item.Data is KohControlObserved existing && existing.IdempotencyKey == idempotencyKey)
            || events.Any(item => item.Data is PenetrationStageCompleted existing && existing.IdempotencyKey == idempotencyKey))
            return false;

        var version = events.Count == 0 ? 0 : events.Max(item => item.Version);
        session.Events.Append(streamId, version, @event);
        await session.SaveChangesAsync(cancellationToken);
        return true;
    }

}
