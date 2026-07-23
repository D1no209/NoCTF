using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Submissions.Processing;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Worker;

public static class BackendMessageHandlers
{
    public static Task Handle(
        EvaluateSubmission message,
        ISubmissionProcessor processor,
        CancellationToken cancellationToken) =>
        processor.ProcessAsync(message.SubmissionId, message.ProcessingVersion, cancellationToken);

    public static Task Handle(
        ProjectLeaderboard message,
        ILeaderboardCache leaderboard,
        CancellationToken cancellationToken) =>
        leaderboard.RefreshAsync(message.CompetitionId, cancellationToken);

    public static async Task<object?> Handle(
        DispatchRuntime message,
        NoCtfDbContext db,
        IChallengeRuntimeTemplateCatalog templates,
        CancellationToken cancellationToken)
    {
        var target = await db.RuntimeInstances
            .Join(
                db.CompetitionChallenges,
                instance => instance.CompetitionChallengeId,
                challenge => challenge.Id,
                (instance, challenge) => new { Instance = instance, Challenge = challenge })
            .Join(
                db.Competitions,
                pair => pair.Instance.CompetitionId,
                competition => competition.Id,
                (pair, competition) => new { pair.Instance, pair.Challenge, Competition = competition })
            .SingleOrDefaultAsync(item => item.Instance.Id == message.RuntimeInstanceId, cancellationToken);
        if (target is null ||
            target.Instance.State != RuntimeState.Queued ||
            target.Instance.ProcessingVersion != message.ProcessingVersion)
            return null;

        var template = templates.Get(target.Competition.Mode, target.Challenge.ConfigurationJson);
        if (template is null || template.Provider == RuntimeProvider.Libvirt)
        {
            target.Instance.State = RuntimeState.Failed;
            target.Instance.FailureCode = RuntimeFailureCode.InvalidConfiguration;
            target.Instance.ProcessingVersion = checked(target.Instance.ProcessingVersion + 1);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        target.Instance.State = RuntimeState.Provisioning;
        target.Instance.ProcessingVersion = checked(target.Instance.ProcessingVersion + 1);
        await db.SaveChangesAsync(cancellationToken);
        var definition = new ContainerRequest(
            target.Instance.Id,
            template.Provider,
            template.Image,
            template.Command ?? [],
            template.Environment ?? new Dictionary<string, string>(),
            MergeLabels(template.Labels, target.Instance),
            template.PortMappings ?? new Dictionary<int, int>(),
            template.Limits ?? new ContainerResourceLimits(512 * 1024 * 1024, 500_000_000, 256),
            template.Security ?? new ContainerSecurityPolicy(true, true, true, ["ALL"], []),
            template.TtlSeconds is > 0 ? TimeSpan.FromSeconds(template.TtlSeconds.Value) : null,
            OperationTimeout: template.OperationTimeoutSeconds is > 0
                ? TimeSpan.FromSeconds(template.OperationTimeoutSeconds.Value)
                : TimeSpan.FromMinutes(2));
        return new ProvisionContainerRuntime(
            target.Instance.Id,
            target.Instance.ProcessingVersion,
            target.Instance.Generation,
            target.Instance.RunnerPool,
            definition);
    }

    public static async Task<object?> Handle(
        StopRuntime message,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null ||
            instance.State != RuntimeState.Stopping ||
            instance.ProcessingVersion != message.ProcessingVersion)
            return null;
        if (string.IsNullOrWhiteSpace(instance.ProviderReceiptJson))
        {
            instance.State = RuntimeState.Stopped;
            instance.StoppedAt = DateTimeOffset.UtcNow;
            instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        return new StopContainerRuntime(
            instance.Id,
            instance.ProcessingVersion,
            instance.RunnerPool,
            instance.RuntimeProvider,
            instance.ProviderReceiptJson);
    }

    public static async Task Handle(
        DrainSubmissions message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        const int batchSize = 500;
        IQueryable<NoCTF.Domain.Submissions.Submission> query;
        if (message.SubmissionId is Guid submissionId)
        {
            query = db.Submissions.FromSqlInterpolated(
                $"""
                SELECT s.*
                FROM submissions AS s
                WHERE s.id = {submissionId}
                  AND s.competition_id = {message.CompetitionId}
                  AND s.competition_challenge_id = {message.CompetitionChallengeId}
                  AND s.received_at <= {message.Cutoff}
                FOR UPDATE SKIP LOCKED
                """);
        }
        else if (message.Rejudge)
        {
            query = db.Submissions.FromSqlInterpolated(
                $"""
                SELECT s.*
                FROM submissions AS s
                WHERE s.competition_id = {message.CompetitionId}
                  AND s.competition_challenge_id = {message.CompetitionChallengeId}
                  AND s.received_at <= {message.Cutoff}
                  AND s.current_scoring_event_id IS NOT NULL
                  AND s.kind IN (0, 1)
                  AND s.evaluation_state <> 1
                  AND s.evaluation_state <> 2
                ORDER BY s.received_at, s.id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """);
        }
        else
        {
            query = db.Submissions.FromSqlInterpolated(
                $"""
                SELECT s.*
                FROM submissions AS s
                WHERE s.competition_id = {message.CompetitionId}
                  AND s.competition_challenge_id = {message.CompetitionChallengeId}
                  AND s.received_at <= {message.Cutoff}
                  AND (
                    s.evaluation_state = 0
                    OR (s.evaluation_state = 4 AND s.current_scoring_event_id IS NULL)
                  )
                ORDER BY s.received_at, s.id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """);
        }

        var submissions = await query.ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var submission in submissions)
        {
            submission.EvaluationState = NoCTF.Domain.Submissions.SubmissionEvaluationState.Queued;
            submission.EvaluationFailureCode = null;
            submission.EvaluationUpdatedAt = now;
            submission.ProcessingVersion = checked(submission.ProcessingVersion + 1);
            await outbox.PublishAsync(new EvaluateSubmission(
                submission.Id,
                submission.ProcessingVersion));
        }
        if (submissions.Count == batchSize && message.SubmissionId is null)
            await outbox.PublishAsync(message);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private static IReadOnlyDictionary<string, string> MergeLabels(
        IReadOnlyDictionary<string, string>? configured,
        RuntimeInstance instance)
    {
        var labels = configured is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(configured, StringComparer.Ordinal);
        labels["noctf.runtime-id"] = instance.Id.ToString("N");
        labels["noctf.competition-id"] = instance.CompetitionId.ToString("N");
        labels["noctf.competition-challenge-id"] = instance.CompetitionChallengeId.ToString("N");
        labels["noctf.generation"] = instance.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (instance.TeamId is Guid teamId)
            labels["noctf.team-id"] = teamId.ToString("N");
        return labels;
    }
}
