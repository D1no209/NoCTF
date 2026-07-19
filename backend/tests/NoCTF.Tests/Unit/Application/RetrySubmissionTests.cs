using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Submissions.Retry;

namespace NoCTF.Tests.Unit.Application;

public class RetrySubmissionTests
{
    [Test]
    public async Task ExecuteAsync_RetiredEvent_QueuesProcessingAndRefresh()
    {
        var scheduler = new Scheduler();
        var useCase = new RetrySubmission(new Store(new(true, true)), scheduler);
        var competitionId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();

        var result = await useCase.ExecuteAsync(competitionId, submissionId, Guid.NewGuid(), false, CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(scheduler.SubmissionId).IsEqualTo(submissionId);
        await Assert.That(scheduler.RefreshCompetitionId).IsEqualTo(competitionId);
    }

    [Test]
    public async Task ExecuteAsync_CorrectSubmissionWithoutOverride_ReturnsFailure()
    {
        var useCase = new RetrySubmission(new Store(new(true, false, "correct_submission_cannot_retry")), new Scheduler());

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false, CancellationToken.None);

        await Assert.That(result.ErrorCode).IsEqualTo("correct_submission_cannot_retry");
    }

    private sealed class Store(SubmissionRetryResult result) : ISubmissionRetryStore
    {
        public Task<SubmissionRetryResult> RetireCurrentEventAsync(Guid competitionId, Guid submissionId, Guid actorId, bool allowCorrect, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class Scheduler : IBackgroundWorkScheduler
    {
        public Guid? SubmissionId { get; private set; }
        public Guid? RefreshCompetitionId { get; private set; }
        public ValueTask EnqueueSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) { SubmissionId = submissionId; return ValueTask.CompletedTask; }
        public ValueTask EnqueueSystemEventAsync(Guid scoringEventId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueLeaderboardRefreshAsync(Guid competitionId, CancellationToken cancellationToken) { RefreshCompetitionId = competitionId; return ValueTask.CompletedTask; }
        public ValueTask EnqueueCompetitionRebuildAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
