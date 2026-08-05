using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.RefreshSession;
using NoCTF.Application.Common;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Status;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Submissions.PatchUploads;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.OpenApi;

internal sealed class SwaggerSubmissionStore : ISubmissionIntakeStore
{
    public Task<SubmissionAdmissionSnapshot?> LoadAdmissionAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.FromResult<SubmissionAdmissionSnapshot?>(null);
    public Task<SubmissionAcceptanceResult> TryAcceptFlagAsync(
        FlagSubmissionReceived received, SubmissionAdmissionSnapshot snapshot, int? maxAttempts,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SubmissionAcceptanceResult(SubmissionAcceptanceState.SnapshotChanged));
    public Task<IReadOnlyList<SubmissionAcceptanceResult>> TryAcceptFlagsAsync(
        IReadOnlyList<FlagSubmissionReceived> received,
        SubmissionAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubmissionAcceptanceResult>>(
            received.Select(_ => new SubmissionAcceptanceResult(
                SubmissionAcceptanceState.SnapshotChanged)).ToArray());
    public Task<SubmissionAcceptanceResult> TryAcceptFixAsync(
        FixSubmissionReceived received, SubmissionAdmissionSnapshot snapshot, int? maxAttempts,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SubmissionAcceptanceResult(SubmissionAcceptanceState.SnapshotChanged));
}

internal sealed class SwaggerPatchUploadStore : IPatchUploadStore
{
    public Task<PatchUploadScope?> ResolveScopeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.FromResult<PatchUploadScope?>(null);
    public Task<bool> SaveAsync(
        Guid patchUploadId,
        PatchUploadScope scope,
        string objectKey,
        string fileName,
        string contentType,
        long byteLength,
        byte[] sha256,
        DateTimeOffset uploadedAt,
        CancellationToken cancellationToken) =>
        Task.FromResult(false);
}

internal sealed class SwaggerFixArchiveReader : IFixArchiveReader
{
    public Task<FixArchiveDescriptor?> FindAsync(
        Guid submissionId,
        CancellationToken cancellationToken) =>
        Task.FromResult<FixArchiveDescriptor?>(null);
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
    public Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<UserProfile?>(null);
    public Task<UserProfile?> UpdateProfileAsync(
        Guid userId,
        string? description,
        bool isEmailPublic,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult<UserProfile?>(null);
    public Task<UserAvatarReplacement?> ReplaceAvatarAsync(
        Guid userId,
        string objectKey,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult<UserAvatarReplacement?>(null);
    public Task<string?> GetAvatarObjectKeyAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
    public Task<CreateUserState> CreateAsync(
        Guid userId,
        string userName,
        string email,
        string password,
        bool emailVerified,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(CreateUserState.Created);
    public Task<ChangePasswordState> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(ChangePasswordState.CurrentPasswordInvalid);
    public Task<bool> IncrementTokenVersionAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(false);
}

internal sealed class SwaggerAccessTokenVersionReader : IAccessTokenVersionReader
{
    public Task<bool> IsCurrentAsync(Guid userId, int tokenVersion, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}

internal sealed class SwaggerAvatarImageProcessor : IAvatarImageProcessor
{
    public AvatarImageProcessingResult Process(ReadOnlyMemory<byte> content) =>
        AvatarImageProcessingResult.Rejected(AvatarImageFailure.MalformedImage);
}

internal sealed class SwaggerEmailVerificationStore : IEmailVerificationStore
{
    public Task<bool> IsRequiredAsync(CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<EmailVerificationState> IssueAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(EmailVerificationState.Issued);
    public Task<EmailVerificationState> VerifyAsync(
        string token,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(EmailVerificationState.Verified);
}

internal sealed class SwaggerTokenIssuer : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(
        AuthenticatedUser user,
        DateTimeOffset now,
        TimeSpan? lifetime = null) =>
        new("swagger-export-token", now.Add(lifetime ?? TimeSpan.FromMinutes(15)));

    public IssuedRefreshToken IssueRefresh(AuthenticatedUser user) =>
        new("swagger-export-refresh-token", DateTimeOffset.UtcNow.AddDays(30));

    public RefreshTokenPrincipal? ValidateRefresh(string token) => null;
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

internal sealed class SwaggerBackendMessagePublisher : IBackendMessagePublisher
{
    public ValueTask ProjectLeaderboardAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask RebuildCompetitionAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask ApplyCompetitionVisibilityAsync(Guid competitionId, int visibilityRevision, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask CleanupCompetitionRuntimesAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask ProvisionCompetitionRuntimesAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

internal sealed class SwaggerLeaderboardCache : ILeaderboardCache
{
    public Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken cancellationToken) => Task.FromResult<LeaderboardResponse?>(null);
    public Task<LeaderboardResponse?> GetFrozenAsync(Guid competitionId, CancellationToken cancellationToken) => Task.FromResult<LeaderboardResponse?>(null);
    public Task RefreshAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task InvalidateAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;
}
