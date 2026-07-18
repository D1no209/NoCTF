using NoCTF.Application.Authentication.Ports;
using NoCTF.Application.Common;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Scoring.Ports;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.OpenApi;

internal sealed class SwaggerSubmissionStore : ISubmissionIntakeStore
{
    public Task<SubmissionAdmissionSnapshot?> LoadAdmissionAsync(Guid competitionId, Guid teamId, Guid challengeId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<SubmissionAdmissionSnapshot?>(null);
    public Task<bool> TryAcceptFlagAsync(FlagSubmissionReceived received, long expectedRevision, CancellationToken cancellationToken) =>
        Task.FromResult(false);
    public Task<bool> TryAcceptFixAsync(FixSubmissionReceived received, long expectedRevision, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}

internal sealed class SwaggerSubmissionQueue : ISubmissionQueue
{
    public Task EnqueueAsync(ProcessFlagSubmission message, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task EnqueueAsync(ProcessFixSubmission message, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class SwaggerStatusReader : ISubmissionStatusReader
{
    public Task<SubmissionStatusView?> FindAsync(Guid competitionId, Guid submissionId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<SubmissionStatusView?>(null);
}

internal sealed class SwaggerAuthenticationStore : IUserAuthenticationStore
{
    public Task<AuthenticatedUser?> FindByLoginAsync(string login, CancellationToken cancellationToken) => Task.FromResult<AuthenticatedUser?>(null);
    public Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<AuthenticatedUser?>(null);
    public Task<bool> VerifyPasswordAsync(Guid userId, string password, CancellationToken cancellationToken) => Task.FromResult(false);
    public Task<string> CreateRefreshTokenAsync(Guid userId, string? ipAddress, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
    public Task<RefreshRotation?> RotateRefreshAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<RefreshRotation?>(null);
    public Task RevokeRefreshFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class SwaggerTokenIssuer : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(AuthenticatedUser user) =>
        new("swagger-export-token", DateTimeOffset.UtcNow.AddMinutes(15));
}

internal sealed class SwaggerModerationStore : ITeamModerationStore
{
    public Task<OperationResult> ApplyAsync(TeamModerationCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Success());
}

internal sealed class SwaggerModerationAuthorizer : ICompetitionModerationAuthorizer
{
    public Task<bool> CanModerateAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}

internal sealed class SwaggerRebuildQueue : IScoringRebuildQueue
{
    public Task EnqueueAsync(Guid competitionId, ScoringRebuildReason reason, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class SwaggerLeaderboardStore : ILeaderboardStore
{
    public Task<LeaderboardSnapshot?> GetAuthoritativeAsync(Guid competitionId, CancellationToken cancellationToken) =>
        Task.FromResult<LeaderboardSnapshot?>(null);
    public Task PublishCacheAsync(LeaderboardSnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyList<Guid>> GetDirtyCompetitionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);
    public Task MarkCleanAsync(Guid competitionId, long projectionVersion, CancellationToken cancellationToken) => Task.CompletedTask;
}
