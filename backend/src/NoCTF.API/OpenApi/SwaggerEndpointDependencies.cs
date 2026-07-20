using NoCTF.Application.Authentication.Ports;
using NoCTF.Application.Common;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.OpenApi;

internal sealed class SwaggerSubmissionStore : ISubmissionIntakeStore
{
    public Task<SubmissionAcceptanceResult?> FindAcceptedAsync(
        Guid competitionId, string idempotencyKey, Guid teamId, Guid challengeId, Guid userId,
        NoCTF.Domain.Submissions.SubmissionKind kind, CancellationToken cancellationToken) =>
        Task.FromResult<SubmissionAcceptanceResult?>(null);
    public Task<SubmissionAdmissionSnapshot?> LoadAdmissionAsync(Guid competitionId, Guid teamId, Guid challengeId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<SubmissionAdmissionSnapshot?>(null);
    public Task<SubmissionAcceptanceResult> TryAcceptFlagAsync(
        FlagSubmissionReceived received, SubmissionAdmissionSnapshot snapshot, int? maxAttempts,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SubmissionAcceptanceResult(SubmissionAcceptanceState.SnapshotChanged));
    public Task<SubmissionAcceptanceResult> TryAcceptFixAsync(
        FixSubmissionReceived received, SubmissionAdmissionSnapshot snapshot, int? maxAttempts,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SubmissionAcceptanceResult(SubmissionAcceptanceState.SnapshotChanged));
}

internal sealed class SwaggerSubmissionAdmissionModePolicy : ISubmissionAdmissionModePolicy
{
    public SubmissionAdmissionRules GetRules(
        GameMode mode,
        string competitionConfigurationJson,
        string challengeConfigurationJson) => new(true, true, null, null);
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
    public Task RevokeRefreshTokenAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class SwaggerAccessTokenVersionReader : IAccessTokenVersionReader
{
    public Task<bool> IsCurrentAsync(Guid userId, int tokenVersion, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}

internal sealed class SwaggerTokenIssuer : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(AuthenticatedUser user) =>
        new("swagger-export-token", DateTimeOffset.UtcNow.AddMinutes(15));
}

internal sealed class SwaggerModerationStore : ITeamModerationStore
{
    public Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken cancellationToken) =>
        Task.FromResult<CompetitionStatus?>(CompetitionStatus.Draft);

    public Task<TeamModerationStoreResult> ApplyAsync(TeamModerationCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(new TeamModerationStoreResult());
}

internal sealed class SwaggerModerationAuthorizer : ICompetitionModerationAuthorizer
{
    public Task<bool> CanModerateAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}

internal sealed class SwaggerBackgroundWorkScheduler : IBackgroundWorkScheduler
{
    public ValueTask EnqueueSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask EnqueueSystemEventAsync(Guid scoringEventId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask EnqueueLeaderboardRefreshAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask EnqueueCompetitionRebuildAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

internal sealed class SwaggerLeaderboardCache : ILeaderboardCache
{
    public Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken cancellationToken) => Task.FromResult<LeaderboardResponse?>(null);
    public Task RefreshAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task InvalidateAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;
}
