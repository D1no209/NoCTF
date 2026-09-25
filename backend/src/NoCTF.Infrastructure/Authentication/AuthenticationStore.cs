using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Storage;
using NoCTF.Domain.Storage;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Storage;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Authentication;

public sealed class AuthenticationStore(
    NoCtfDbContext db,
    IPasswordHasher<User> passwordHasher,
    IPostCommitMessagePublisher? messageOutbox = null,
    FileReferenceLock? fileReferenceLock = null,
    TimeProvider? clock = null,
    IEmailVerificationConfigurationStore? emailVerificationConfiguration = null,
    ILogger<AuthenticationStore>? logger = null,
    NoCTF.Application.Authentication.Privacy.IRequestSourceAddress? source = null)
    : IUserAuthenticationStore,
        IUserRegistrationStore,
        ICurrentUserProfilePatchStore
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    private readonly IPostCommitMessagePublisher outbox =
        messageOutbox ?? new NoOpPostCommitMessagePublisher();
    private readonly FileReferenceLock fileLock = fileReferenceLock ?? new FileReferenceLock();
    public async Task<AuthenticatedUser?> FindByLoginAsync(
        string login,
        CancellationToken ct)
    {
        var normalizedUserName = login.Trim().ToUpperInvariant();
        var canonicalEmail = EmailCanonicalizer.Canonicalize(login);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            item => item.Kind == UserKind.Human
                && item.AccountStatus == UserAccountStatus.Active
                && (item.Email == canonicalEmail
                || item.NormalizedUserName == normalizedUserName
                || item.Id.ToString() == login),
            ct);
        return ToAuthenticated(user);
    }

    public async Task<bool> VerifyPasswordAsync(
        Guid userId,
        string password,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == userId && item.AccountStatus == UserAccountStatus.Active,
            ct);
        if (user is null || user.Kind != UserKind.Human)
            return false;
        if (!IsCurrentPasswordHash(user.PasswordHash))
            return false;
        return passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password)
            == PasswordVerificationResult.Success;
    }

    private static bool IsCurrentPasswordHash(string hash)
    {
        try
        {
            var decoded = Convert.FromBase64String(hash);
            return decoded.Length > 0 && decoded[0] == 0x01;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public async Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken ct) =>
        ToAuthenticated(await db.Users.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == userId && item.AccountStatus == UserAccountStatus.Active,
            ct));

    public Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId
                && user.AccountStatus == UserAccountStatus.Active)
            .Select(user => new UserProfile(
                user.Id,
                user.UserName,
                user.Email,
                user.Role,
                user.Kind,
                user.EmailVerifiedAt != null,
                user.Description,
                user.AvatarFileId,
                user.WallpaperFileId,
                user.WallpaperEnabled,
                user.SchoolFullName,
                user.SchoolStudentNumber,
                user.ProfileCoverFileId))
            .SingleOrDefaultAsync(ct);

    public async Task<PublicUserProfile?> GetPublicProfileAsync(
        Guid userId,
        CancellationToken ct)
    {
        var profile = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId
                && user.AccountStatus == UserAccountStatus.Active)
            .Select(user => new PublicUserProfile(
                user.Id,
                user.UserName,
                user.Description,
                user.AvatarFileId,
                user.ProfileCoverFileId))
            .SingleOrDefaultAsync(ct);
        if (profile is null)
            return null;

        var publicCompetitions = db.Competitions.AsNoTracking()
            .Where(competition => competition.AccessMode == CompetitionAccessMode.Public
                && competition.Status != CompetitionStatus.Draft
                && competition.DeletedAt == null);
        var eligibleTeams = db.Teams.AsNoTracking()
            .Where(team => team.Members.Any(member => member.UserId == userId)
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && !team.IsBanned
                && team.DeletedAt == null);

        var participations = await eligibleTeams
            .Join(
                publicCompetitions,
                team => team.CompetitionId,
                competition => competition.Id,
                (team, competition) => new
                {
                    CompetitionId = competition.Id,
                    competition.Title,
                    TeamName = team.Name,
                    competition.Mode,
                    competition.Status,
                    competition.StartAt,
                    competition.EndAt
                })
            .OrderByDescending(item => item.EndAt)
            .ThenByDescending(item => item.StartAt)
            .Select(item => new PublicUserCompetitionSummary(
                item.CompetitionId,
                item.Title,
                item.TeamName,
                item.Mode,
                item.Status,
                item.StartAt,
                item.EndAt))
            .ToListAsync(ct);

        var directionRows = await db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.ActorUserId == userId
                && fact.State == GameplayFactState.Completed
                && fact.TeamId != null
                && ((fact.Kind == GameplayFactKind.FlagAttempt
                        || fact.Kind == GameplayFactKind.BreakAttempt)
                    && fact.Result == GameplayFactResult.Correct
                    || fact.Kind == GameplayFactKind.FixAttempt
                    && fact.Result == GameplayFactResult.Applied
                    || fact.Kind == GameplayFactKind.KohControlObservation
                    && fact.Result == GameplayFactResult.Controlled))
            .Join(
                eligibleTeams,
                fact => fact.TeamId,
                team => (Guid?)team.Id,
                (fact, team) => fact)
            .Join(
                publicCompetitions,
                fact => fact.CompetitionId,
                competition => competition.Id,
                (fact, competition) => fact)
            .Join(
                db.CompetitionChallenges.AsNoTracking()
                    .Where(item => item.IsPublished && item.DeletedAt == null),
                fact => fact.CompetitionChallengeId,
                competitionChallenge => competitionChallenge.Id,
                (fact, competitionChallenge) => new
                {
                    fact.CompetitionChallengeId,
                    competitionChallenge.ChallengeId
                })
            .Join(
                db.Challenges.AsNoTracking().Where(challenge => challenge.DeletedAt == null),
                item => item.ChallengeId,
                challenge => challenge.Id,
                (item, challenge) => new
                {
                    item.CompetitionChallengeId,
                    challenge.Direction
                })
            .GroupBy(item => item.Direction)
            .Select(group => new
            {
                Direction = group.Key,
                SuccessfulChallengeCount = group
                    .Select(item => item.CompetitionChallengeId)
                    .Distinct()
                    .Count()
            })
            .OrderByDescending(item => item.SuccessfulChallengeCount)
            .ThenBy(item => item.Direction)
            .ToListAsync(ct);
        var directionStats = directionRows
            .Select(item => new PublicUserDirectionSummary(
                item.Direction,
                item.SuccessfulChallengeCount))
            .ToList();

        var modeStats = participations
            .GroupBy(item => item.Mode)
            .Select(group => new PublicUserModeSummary(group.Key, group.Count()))
            .OrderByDescending(item => item.CompetitionCount)
            .ThenBy(item => item.Mode)
            .ToList();

        return profile with
        {
            Modes = modeStats,
            Directions = directionStats,
            RecentCompetitions = participations.Take(5).ToList(),
            CompetitionCount = participations.Count,
            FinishedCompetitionCount = participations.Count(item =>
                item.Status == CompetitionStatus.Finished),
            SuccessfulChallengeCount = directionStats.Sum(item =>
                item.SuccessfulChallengeCount)
        };
    }

    public async Task<UserProfile?> UpdateProfileAsync(
        Guid userId,
        string? description,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(item =>
            item.Id == userId && item.AccountStatus == UserAccountStatus.Active,
            ct);
        if (user is null)
            return null;

        user.Description = description;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return ToProfile(user);
    }

    public Task<User?> FindForPatchAsync(Guid userId, CancellationToken ct) =>
        db.Users.SingleOrDefaultAsync(user =>
            user.Id == userId && user.AccountStatus == UserAccountStatus.Active,
            ct);

    public Task SaveAsync(User user, CancellationToken ct) => db.SaveChangesAsync(ct);

    public void DiscardChanges() => db.ChangeTracker.Clear();

    public async Task<UserAvatarReplacement?> ReplaceAvatarAsync(
        Guid userId,
        Guid fileId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(item =>
            item.Id == userId && item.AccountStatus == UserAccountStatus.Active,
            ct);
        if (user is null)
            return null;
        if (!await fileLock.AcquireAsync(db, fileId, ct))
            return null;

        var previousFileId = user.AvatarFileId;
        user.AvatarFileId = fileId;
        user.UpdatedAt = now;
        if (previousFileId is { } previous && previous != fileId)
            await outbox.PublishAsync(new CleanupFile(previous));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
        return new UserAvatarReplacement(ToProfile(user), previousFileId);
    }

    public Task<BusinessFileReference?> GetAvatarFileAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId
                && user.AccountStatus == UserAccountStatus.Active
                && user.AvatarFileId != null)
            .Join(db.Files.AsNoTracking(), user => user.AvatarFileId, file => file.Id,
                (_, file) => new BusinessFileReference(
                    file.Id, file.ObjectKey, file.FileName, file.ContentType))
            .SingleOrDefaultAsync(ct);

    public async Task<UserProfileCoverReplacement?> ReplaceProfileCoverAsync(
        Guid userId,
        Guid fileId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(item =>
            item.Id == userId && item.AccountStatus == UserAccountStatus.Active,
            ct);
        if (user is null)
            return null;
        if (!await fileLock.AcquireAsync(db, fileId, ct))
            return null;

        var previousFileId = user.ProfileCoverFileId;
        user.ProfileCoverFileId = fileId;
        user.UpdatedAt = now;
        if (previousFileId is { } previous && previous != fileId)
            await outbox.PublishAsync(new CleanupFile(previous));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
        return new UserProfileCoverReplacement(ToProfile(user), previousFileId);
    }

    public Task<BusinessFileReference?> GetProfileCoverFileAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId
                && user.AccountStatus == UserAccountStatus.Active
                && user.ProfileCoverFileId != null)
            .Join(db.Files.AsNoTracking(), user => user.ProfileCoverFileId, file => file.Id,
                (_, file) => new BusinessFileReference(
                    file.Id, file.ObjectKey, file.FileName, file.ContentType))
            .SingleOrDefaultAsync(ct);

    public async Task<UserWallpaperReplacement?> ReplaceWallpaperAsync(
        Guid userId,
        Guid fileId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(item =>
            item.Id == userId && item.AccountStatus == UserAccountStatus.Active,
            ct);
        if (user is null)
            return null;
        if (!await fileLock.AcquireAsync(db, fileId, ct))
            return null;

        var previousFileId = user.WallpaperFileId;
        user.WallpaperFileId = fileId;
        user.WallpaperEnabled = true;
        user.UpdatedAt = now;
        if (previousFileId is { } previous && previous != fileId)
            await outbox.PublishAsync(new CleanupFile(previous));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
        return new UserWallpaperReplacement(ToProfile(user), previousFileId);
    }

    public async Task<UserWallpaperPreferenceResult> SetWallpaperEnabledAsync(
        Guid userId,
        bool enabled,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(item =>
            item.Id == userId && item.AccountStatus == UserAccountStatus.Active,
            ct);
        if (user is null)
            return new(UserWallpaperPreferenceState.UserNotFound);
        if (enabled && user.WallpaperFileId is null)
            return new(UserWallpaperPreferenceState.WallpaperNotUploaded);

        user.WallpaperEnabled = enabled;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new(UserWallpaperPreferenceState.Updated, ToProfile(user));
    }

    public Task<BusinessFileReference?> GetWallpaperFileAsync(
        Guid userId,
        CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId
                && user.AccountStatus == UserAccountStatus.Active
                && user.WallpaperFileId != null)
            .Join(db.Files.AsNoTracking(), user => user.WallpaperFileId, file => file.Id,
                (_, file) => new BusinessFileReference(
                    file.Id, file.ObjectKey, file.FileName, file.ContentType))
            .SingleOrDefaultAsync(ct);

    public async Task<CreateUserState> CreateAsync(
        Guid userId,
        string userName,
        string email,
        string password,
        bool emailVerified,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var trimmedUserName = userName.Trim();
        var canonicalEmail = EmailCanonicalizer.Canonicalize(email);
        var normalizedUserName = trimmedUserName.ToUpperInvariant();
        if (await db.Users.AnyAsync(user => user.NormalizedUserName == normalizedUserName, ct))
            return CreateUserState.UserNameConflict;
        if (await db.Users.AnyAsync(user => user.Email == canonicalEmail, ct))
            return CreateUserState.EmailConflict;

        var user = CreateUser(
            userId,
            trimmedUserName,
            normalizedUserName,
            canonicalEmail,
            password,
            emailVerified,
            now);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
            return CreateUserState.Created;
        }
        catch (DbUpdateException)
        {
            db.Entry(user).State = EntityState.Detached;
            return await db.Users.AnyAsync(
                existing => existing.Email == canonicalEmail, ct)
                ? CreateUserState.EmailConflict
                : CreateUserState.UserNameConflict;
        }
    }

    public async Task<CreateRegisteredUserResult> RegisterAsync(
        Guid userId,
        string userName,
        string email,
        string password,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var trimmedUserName = userName.Trim();
        var canonicalEmail = EmailCanonicalizer.Canonicalize(email);
        var normalizedUserName = trimmedUserName.ToUpperInvariant();
        var settings = emailVerificationConfiguration is null
            ? null
            : await emailVerificationConfiguration.GetAsync(ct);
        var verificationState = settings?.Enabled == true
            ? DeliveryConfigured(settings)
                ? EmailVerificationState.Issued
                : EmailVerificationState.DeliveryNotConfigured
            : EmailVerificationState.Disabled;

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct);
        if (await db.Users.AnyAsync(user => user.NormalizedUserName == normalizedUserName, ct))
            return new(CreateUserState.UserNameConflict, verificationState);
        if (await db.Users.AnyAsync(user => user.Email == canonicalEmail, ct))
            return new(CreateUserState.EmailConflict, verificationState);

        var user = CreateUser(
            userId,
            trimmedUserName,
            normalizedUserName,
            canonicalEmail,
            password,
            emailVerified: verificationState == EmailVerificationState.Disabled,
            now);
        db.Users.Add(user);

        string? verificationToken = null;
        db.Notifications.Add(Privacy.AuthenticationActivity.Create(userId,
            NoCTF.Application.Authentication.Privacy.AccountActivityKind.Registered, source?.Address, now));
        if (verificationState == EmailVerificationState.Issued)
        {
            verificationToken = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
                RandomNumberGenerator.GetBytes(32));
            db.AccountTokens.Add(new AccountToken
            {
                Id = Guid.CreateVersion7(now),
                UserId = userId,
                Kind = AccountTokenKind.EmailVerification,
                TokenSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(verificationToken)),
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(settings!.TokenLifetimeMinutes)
            });
        }

        try
        {
            // Persist the user and token before publishing so a uniqueness failure cannot
            // leave an in-memory envelope for an account that was never created. The
            // transaction remains open and the scoped Wolverine outbox permits the second
            // flush that atomically stores the envelope below.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new(
                await db.Users.AnyAsync(existing => existing.Email == canonicalEmail, ct)
                    ? CreateUserState.EmailConflict
                    : CreateUserState.UserNameConflict,
                verificationState);
        }

        if (verificationToken is not null)
        {
            await outbox.PublishAsync(new SendEmailVerification(userId, verificationToken));
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        if (verificationToken is not null)
            await outbox.FlushCommittedMessagesAsync();
        ObserveRegistrationVerification(verificationState, userId);
        return new(CreateUserState.Created, verificationState);
    }

    public async Task<ChangePasswordState> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == userId && item.AccountStatus == UserAccountStatus.Active,
            ct);
        if (user is null
            || user.Kind != UserKind.Human
            || !IsCurrentPasswordHash(user.PasswordHash)
            || passwordHasher.VerifyHashedPassword(
                user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            return ChangePasswordState.CurrentPasswordInvalid;

        var replacement = passwordHasher.HashPassword(user, newPassword);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        if (!await UserCredentialWrite.ReplaceAsync(db, user, replacement, invalidateTokens: true, now, ct))
            return ChangePasswordState.CurrentPasswordInvalid;
        await UserCredentialWrite.InvalidateResetTokensAsync(db, user.Id, null, now, ct);
        await outbox.PublishAsync(new SendPasswordChangedNotification(user.Id));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
        return ChangePasswordState.Changed;
    }

    public async Task<bool> IncrementTokenVersionAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct) =>
        await db.Users.Where(user =>
                user.Id == userId && user.AccountStatus == UserAccountStatus.Active)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.TokenVersion, user => user.TokenVersion + 1)
                    .SetProperty(user => user.UpdatedAt, now),
                ct) == 1;

    private static AuthenticatedUser? ToAuthenticated(User? user) =>
        user is null
            ? null
            : new(
                user.Id,
                user.UserName,
                user.Role,
                user.Kind,
                user.TokenVersion,
                user.EmailVerifiedAt is not null);

    private static UserProfile ToProfile(User user) =>
        new(
            user.Id,
            user.UserName,
            user.Email,
            user.Role,
            user.Kind,
            user.EmailVerifiedAt is not null,
            user.Description,
            user.AvatarFileId,
            user.WallpaperFileId,
            user.WallpaperEnabled,
            user.SchoolFullName,
            user.SchoolStudentNumber,
            user.ProfileCoverFileId);

    private User CreateUser(
        Guid userId,
        string userName,
        string normalizedUserName,
        string email,
        string password,
        bool emailVerified,
        DateTimeOffset now)
    {
        var user = new User
        {
            Id = userId,
            UserName = userName,
            NormalizedUserName = normalizedUserName,
            Email = email,
            Kind = UserKind.Human,
            Role = UserRole.User,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = emailVerified ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        return user;
    }

    private static bool DeliveryConfigured(EmailVerificationConfigurationView settings) =>
        !string.IsNullOrWhiteSpace(settings.SmtpHost)
        && !string.IsNullOrWhiteSpace(settings.SmtpFromAddress)
        && (string.IsNullOrWhiteSpace(settings.SmtpUserName)
            || settings.SmtpPasswordConfigured);

    private void ObserveRegistrationVerification(
        EmailVerificationState state,
        Guid userId)
    {
        NoCtfTelemetry.RecordAccountNotificationIssuance(
            "email_verification",
            state.ToString());
        logger?.LogInformation(
            "Registration email verification issuance completed with {Outcome} for user {UserId}.",
            state,
            userId);
    }
}
