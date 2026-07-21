using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Submissions.Retry;
using NoCTF.Domain.Competitions;

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
        var useCase = new RetrySubmission(new Store(new(true, false, SubmissionRetryFailure.CorrectSubmissionCannotRetry)), new Scheduler());

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false, CancellationToken.None);

        await Assert.That(result.ErrorCode).IsEqualTo("correct_submission_cannot_retry");
    }

    [Test]
    public async Task ExecuteAsync_FinishedCompetition_DoesNotRetireOrQueue()
    {
        var store = new Store(new(true, true)) { Status = CompetitionStatus.Finished };
        var scheduler = new Scheduler();

        var result = await new RetrySubmission(store, scheduler).ExecuteAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true, CancellationToken.None);

        await Assert.That(result.ErrorCode).IsEqualTo("competition_finished");
        await Assert.That(store.RetireCalls).IsEqualTo(0);
        await Assert.That(scheduler.SubmissionId).IsNull();
    }

    private sealed class Store(SubmissionRetryResult result) : ISubmissionRetryStore
    {
        public CompetitionStatus? Status { get; init; } = CompetitionStatus.Running;
        public int RetireCalls { get; private set; }
        public Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken cancellationToken) => Task.FromResult(Status);
        public Task<SubmissionRetryResult> RetireCurrentEventAsync(Guid competitionId, Guid submissionId, Guid actorId, bool allowCorrect, CancellationToken cancellationToken)
        {
            RetireCalls++;
            return Task.FromResult(result);
        }
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
