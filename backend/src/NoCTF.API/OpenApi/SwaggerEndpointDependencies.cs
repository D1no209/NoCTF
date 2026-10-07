using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.RefreshSession;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Application.Common;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Application.Storage;

namespace NoCTF.API.OpenApi;

internal sealed class SwaggerGameplayFactStore : IGameplayFactIntakeStore
{
    public Task<GameplayFactAdmissionSnapshot?> LoadAdmissionAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.FromResult<GameplayFactAdmissionSnapshot?>(null);
    public Task<GameplayFactAcceptanceResult> TryAcceptFlagAsync(
        FlagGameplayFactReceived received, GameplayFactAdmissionSnapshot snapshot, int? maxAttempts,
        CancellationToken cancellationToken) =>
        Task.FromResult(new GameplayFactAcceptanceResult(GameplayFactAcceptanceState.AdmissionRejected));
    public Task<IReadOnlyList<GameplayFactAcceptanceResult>> TryAcceptFlagsAsync(
        IReadOnlyList<FlagGameplayFactReceived> received,
        GameplayFactAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GameplayFactAcceptanceResult>>(
            received.Select(_ => new GameplayFactAcceptanceResult(
                GameplayFactAcceptanceState.AdmissionRejected)).ToArray());
    public Task<GameplayFactAcceptanceResult> TryAcceptHintUnlockAsync(
        HintUnlockGameplayFactReceived received,
        CancellationToken cancellationToken) =>
        Task.FromResult(new GameplayFactAcceptanceResult(GameplayFactAcceptanceState.AdmissionRejected));
    public Task<GameplayFactAcceptanceResult> TryAcceptManualAdjustmentAsync(
        ManualAdjustmentGameplayFactReceived received,
        CancellationToken cancellationToken) =>
        Task.FromResult(new GameplayFactAcceptanceResult(GameplayFactAcceptanceState.AdmissionRejected));
}

internal sealed class SwaggerPatchUploadStore : IPatchUploadStore
{
    public Task<PatchUploadScope?> ResolveScopeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid runtimeInstanceId,
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.FromResult<PatchUploadScope?>(null);
    public Task<PatchUploadSaveResult> SaveAsync(
        Guid patchUploadId,
        Guid gameplayFactId,
        PatchUploadScope scope,
        Guid fileId,
        DateTimeOffset uploadedAt,
        CancellationToken cancellationToken) =>
        Task.FromResult(new PatchUploadSaveResult(
            PatchUploadSaveState.ConcurrencyConflict));
}

internal sealed class SwaggerAwdpDefenseTargetStore : IAwdpDefenseTargetStore
{
    public Task<AwdpDefenseTargetRequestResult> TryCreateAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AwdpDefenseTargetRequestResult(
            AwdpDefenseTargetRequestState.Created,
            Guid.Empty,
            RuntimeState.Queued));
}

internal sealed class SwaggerFixArchiveReader : IFixArchiveReader
{
    public Task<FixArchiveDescriptor?> FindAsync(
        Guid gameplayFactId,
        CancellationToken cancellationToken) =>
        Task.FromResult<FixArchiveDescriptor?>(null);
}

internal sealed class SwaggerGameplayFactAdmissionModePolicy : IGameplayFactAdmissionModePolicy
{
    public GameplayFactAdmissionRules GetRules(
        GameMode mode,
        CompetitionModeConfiguration competitionConfiguration,
        CompetitionChallengeRules challengeRules,
        ChallengeDefinition? challengeDefinition = null) => new(true, true, null, null);
}

internal sealed class SwaggerStatusReader : IGameplayFactStatusReader
{
    public Task<GameplayFactStatusView?> FindAsync(Guid competitionId, Guid gameplayFactId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<GameplayFactStatusView?>(null);
}

internal sealed class SwaggerAuthenticationStore
    : IUserAuthenticationStore, ICurrentUserProfilePatchStore
{
    public Task<AuthenticatedUser?> FindByLoginAsync(string login, CancellationToken cancellationToken) => Task.FromResult<AuthenticatedUser?>(null);
    public Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<AuthenticatedUser?>(null);
    public Task<bool> VerifyPasswordAsync(Guid userId, string password, CancellationToken cancellationToken) => Task.FromResult(false);
    public Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<UserProfile?>(null);
    public Task<UserProfile?> UpdateProfileAsync(
        Guid userId,
        string? description,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult<UserProfile?>(null);
    public Task<UserAvatarReplacement?> ReplaceAvatarAsync(
        Guid userId,
        Guid fileId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult<UserAvatarReplacement?>(null);
    public Task<BusinessFileReference?> GetAvatarFileAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.FromResult<BusinessFileReference?>(null);
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

    public Task<NoCTF.Domain.Identity.User?> FindForPatchAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.FromResult<NoCTF.Domain.Identity.User?>(null);

    public Task SaveAsync(
        NoCTF.Domain.Identity.User user,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public void DiscardChanges() { }
}

internal sealed class SwaggerAccessTokenVersionReader : IAccessTokenVersionReader
{
    public Task<bool> IsCurrentAsync(
        Guid userId,
        int tokenVersion,
        CancellationToken cancellationToken) =>
        Task.FromResult(false);
}

internal sealed class SwaggerAvatarImageProcessor : IAvatarImageProcessor
{
    public AvatarImageProcessingResult Process(ReadOnlyMemory<byte> content) =>
        AvatarImageProcessingResult.Rejected(AvatarImageFailure.MalformedImage);
}

internal sealed class SwaggerWallpaperImageProcessor : IWallpaperImageProcessor
{
    public WallpaperImageProcessingResult Process(ReadOnlyMemory<byte> content) =>
        WallpaperImageProcessingResult.Rejected(WallpaperImageFailure.MalformedImage);
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
    public Task<EmailVerificationState> IssueByEmailAsync(
        string email,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(EmailVerificationState.Issued);
    public Task<EmailVerificationState> VerifyAsync(
        string token,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(EmailVerificationState.Verified);
}

internal sealed class SwaggerPasswordResetStore : IPasswordResetStore
{
    public Task<PasswordResetRequestState> IssueAsync(
        string email,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(PasswordResetRequestState.Ignored);

    public Task<PasswordResetCompletionState> CompleteAsync(
        string token,
        string newPassword,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(PasswordResetCompletionState.InvalidOrExpired);
}

internal sealed class SwaggerTokenIssuer : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(
        AuthenticatedUser user,
        NoCTF.Domain.Identity.Mfa.AuthenticationContext authentication,
        DateTimeOffset now,
        TimeSpan? lifetime = null) =>
        new("swagger-export-token", now.Add(lifetime ?? TimeSpan.FromMinutes(15)));

    public IssuedRefreshToken IssueRefresh(AuthenticatedUser user, NoCTF.Domain.Identity.Mfa.AuthenticationContext authentication) =>
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
    public ValueTask ApplyCompetitionVisibilityAsync(
        Guid competitionId,
        DateTimeOffset scheduledAt,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask CleanupCompetitionRuntimesAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask ProvisionCompetitionRuntimesAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

internal sealed class SwaggerLeaderboardCache : ILeaderboardCache
{
    public Task RefreshAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task InvalidateAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;
}
