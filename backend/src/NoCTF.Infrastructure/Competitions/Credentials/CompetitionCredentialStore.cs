using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Credentials;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Credentials;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.Infrastructure.Competitions.Credentials;

public sealed class CompetitionCredentialStore(
    NoCtfDbContext db,
    IPasswordHasher<User> passwordHasher,
    PlatformSecretProtector secrets,
    ICompetitionModerationAuthorizer authorizer,
    ICompetitionEventRecorder eventRecorder,
    TimeProvider? clock = null) : ICompetitionCredentialStore
{
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;

    public async Task<CompetitionCredentialImportResult> ImportAsync(
        CompetitionCredentialImportCommand command,
        CancellationToken ct)
    {
        if (!await authorizer.CanJudgeAsync(command.ActorUserId, command.CompetitionId, ct))
            return Failure(CompetitionCredentialImportFailure.Forbidden);
        if (command.Rows.Count == 0)
            return Failure(CompetitionCredentialImportFailure.EmptyFile);

        var competition = await db.Competitions
            .Include(value => value.Tracks)
            .SingleOrDefaultAsync(value => value.Id == command.CompetitionId && value.DeletedAt == null, ct);
        if (competition is null)
            return Failure(CompetitionCredentialImportFailure.CompetitionNotFound);
        if (competition.Status == CompetitionStatus.Finished)
            return Failure(CompetitionCredentialImportFailure.CompetitionFinished);

        var issues = ValidateRows(command.Rows, competition);
        if (issues.Count > 0)
            return new(null, CompetitionCredentialImportFailure.InvalidRows, issues, 0, 0, 0);

        var usernames = command.Rows.Select(row => row.UserName.Trim().ToUpperInvariant()).ToArray();
        var emails = command.Rows.Where(row => !string.IsNullOrWhiteSpace(row.Email))
            .Select(row => EmailCanonicalizer.Canonicalize(row.Email!))
            .Where(value => value.Length > 0)
            .Select(value => value.ToUpperInvariant())
            .ToArray();
        if (await db.Users.AnyAsync(user => usernames.Contains(user.NormalizedUserName), ct))
            return Failure(CompetitionCredentialImportFailure.UserNameConflict);
        if (emails.Length > 0 && await db.Users.AnyAsync(user => emails.Contains(user.NormalizedEmail), ct))
            return Failure(CompetitionCredentialImportFailure.EmailConflict);

        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);
        var existingTeamNames = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == command.CompetitionId && team.DeletedAt == null)
            .Select(team => team.NormalizedName)
            .ToArrayAsync(ct);
        var normalizedTeamNames = command.Rows.Select(row => row.TeamName.Trim().ToUpperInvariant()).Distinct().ToArray();
        if (existingTeamNames.Intersect(normalizedTeamNames, StringComparer.Ordinal).Any())
            return Failure(CompetitionCredentialImportFailure.DuplicateTeam);

        var batch = new CompetitionCredentialBatch
        {
            Id = Guid.CreateVersion7(command.Now),
            CompetitionId = command.CompetitionId,
            CreatedByUserId = command.ActorUserId,
            SourceFileName = Path.GetFileName(command.SourceFileName),
            SourceSha256 = command.SourceSha256,
            CreatedAt = command.Now,
            UpdatedAt = command.Now
        };
        db.CompetitionCredentialBatches.Add(batch);

        var usersByRow = new Dictionary<int, (User User, string Password)>();
        foreach (var row in command.Rows)
        {
            var userId = Guid.CreateVersion7(command.Now.AddTicks(row.RowNumber));
            var password = GeneratePassword();
            var email = row.Email is null ? string.Empty : EmailCanonicalizer.Canonicalize(row.Email);
            var user = new User
            {
                Id = userId,
                UserName = row.UserName.Trim(),
                Email = email,
                Kind = UserKind.Human,
                Role = UserRole.User,
                AccountStatus = UserAccountStatus.Active,
                EmailVerifiedAt = command.Now,
                CreatedAt = command.Now,
                UpdatedAt = command.Now
            };
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            db.Users.Add(user);
            usersByRow[row.RowNumber] = (user, password);
        }

        foreach (var group in command.Rows.GroupBy(row => row.TeamName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var track = ResolveTrack(competition, first.TrackKey);
            if (track is null)
                return Failure(CompetitionCredentialImportFailure.InvalidTrack);
            var captain = group.Single(row => row.Captain);
            var team = new Team
            {
                Id = Guid.NewGuid(),
                CompetitionId = competition.Id,
                TrackKey = track.Key,
                Name = group.Key,
                CaptainId = usersByRow[captain.RowNumber].User.Id,
                InvitationToken = CreateInvitationToken(),
                RegistrationStatus = TeamRegistrationStatus.Approved,
                RegisteredAt = command.Now,
                Members = group.Select(row => new TeamMember
                {
                    UserId = usersByRow[row.RowNumber].User.Id
                }).ToList()
            };
            db.Teams.Add(team);
            await eventRecorder.RecordAsync(new(
                team.CompetitionId,
                CompetitionEventKind.TeamRegistered,
                CompetitionEventLevel.Information,
                track.IsInternal ? CompetitionEventVisibility.Staff : CompetitionEventVisibility.Public,
                command.Now,
                ActorUserId: command.ActorUserId,
                TeamId: team.Id,
                TeamRegistrationStatus: TeamRegistrationStatus.Approved,
                TrackKey: track.Key), ct);
            foreach (var row in group)
            {
                var user = usersByRow[row.RowNumber];
                batch.Credentials.Add(new CompetitionCredential
                {
                    Id = Guid.NewGuid(),
                    BatchId = batch.Id,
                    CompetitionId = competition.Id,
                    UserId = user.User.Id,
                    TeamId = team.Id,
                    PasswordCiphertext = secrets.Protect(
                        user.Password,
                        PlatformSecretPurpose.CompetitionCredentialPassword,
                        batch.Id,
                        user.User.Id),
                    PasswordUpdatedAt = command.Now,
                    CreatedAt = command.Now,
                    UpdatedAt = command.Now
                });
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(batch.Id, CompetitionCredentialImportFailure.None, [], usersByRow.Count,
            batch.Credentials.Select(value => value.TeamId).Distinct().Count(), batch.Credentials.Count);
    }

    public async Task<CompetitionCredentialBatchView?> ReadBatchAsync(
        CompetitionCredentialBatchRequest request,
        CancellationToken ct)
    {
        if (!await authorizer.CanJudgeAsync(request.ActorUserId, request.CompetitionId, ct))
            return null;
        var batch = await db.CompetitionCredentialBatches.AsNoTracking()
            .Include(value => value.Credentials)
            .ThenInclude(value => value.Batch)
            .SingleOrDefaultAsync(value => value.Id == request.BatchId && value.CompetitionId == request.CompetitionId, ct);
        if (batch is null)
            return null;
        var users = await db.Users.AsNoTracking()
            .Where(user => batch.Credentials.Select(value => value.UserId).Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, ct);
        var teams = await db.Teams.AsNoTracking()
            .Where(team => batch.Credentials.Select(value => value.TeamId).Contains(team.Id))
            .ToDictionaryAsync(team => team.Id, ct);
        return new(
            batch.Id,
            batch.CompetitionId,
            batch.CreatedAt,
            batch.SourceFileName,
            batch.Credentials.Count == 0 ? 0 : batch.Credentials.Max(value => value.PasswordVersion),
            batch.Credentials.OrderBy(value => value.TeamId).ThenBy(value => value.UserId).Select(value =>
            {
                var user = users[value.UserId];
                var team = teams[value.TeamId];
                return new CompetitionCredentialDownloadRow(
                    user.Id,
                    user.UserName,
                    string.IsNullOrEmpty(user.Email) ? null : user.Email,
                    team.Name,
                    team.CaptainId == user.Id,
                    team.TrackKey,
                    secrets.Unprotect(value.PasswordCiphertext, PlatformSecretPurpose.CompetitionCredentialPassword, batch.Id, user.Id));
            }).ToArray());
    }

    public async Task<IReadOnlyList<CompetitionCredentialBatchSummary>> ListBatchesAsync(
        Guid competitionId,
        Guid actorUserId,
        CancellationToken ct)
    {
        if (!await authorizer.CanJudgeAsync(actorUserId, competitionId, ct))
            return [];
        return await db.CompetitionCredentialBatches.AsNoTracking()
            .Where(batch => batch.CompetitionId == competitionId)
            .OrderByDescending(batch => batch.CreatedAt)
            .Select(batch => new CompetitionCredentialBatchSummary(
                batch.Id,
                batch.CompetitionId,
                batch.CreatedAt,
                batch.SourceFileName,
                batch.Credentials.Count,
                batch.Credentials.Select(value => (int?)value.PasswordVersion).Max() ?? 0))
            .ToArrayAsync(ct);
    }

    public async Task<CompetitionCredentialMutationResult> RotateBatchAsync(
        CompetitionCredentialBatchRequest request,
        CancellationToken ct)
    {
        if (!await authorizer.CanJudgeAsync(request.ActorUserId, request.CompetitionId, ct))
            return new(CompetitionCredentialImportFailure.Forbidden);
        var batch = await db.CompetitionCredentialBatches
            .Include(value => value.Credentials)
            .SingleOrDefaultAsync(value => value.Id == request.BatchId && value.CompetitionId == request.CompetitionId, ct);
        if (batch is null)
            return new(CompetitionCredentialImportFailure.CredentialNotFound);
        var users = await db.Users
            .Where(user => batch.Credentials.Select(value => value.UserId).Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var credential in batch.Credentials)
        {
            var user = users[credential.UserId];
            var password = GeneratePassword();
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            user.TokenVersion = checked(user.TokenVersion + 1);
            user.UpdatedAt = request.Now;
            credential.PasswordCiphertext = secrets.Protect(password, PlatformSecretPurpose.CompetitionCredentialPassword, batch.Id, user.Id);
            credential.PasswordVersion++;
            credential.PasswordUpdatedAt = request.Now;
            credential.UpdatedAt = request.Now;
        }
        batch.UpdatedAt = request.Now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(CompetitionCredentialImportFailure.None);
    }

    public async Task<CompetitionCredentialMutationResult> RotateUserAsync(
        CompetitionCredentialUserRequest request,
        CancellationToken ct)
    {
        if (!await authorizer.CanJudgeAsync(request.ActorUserId, request.CompetitionId, ct))
            return new(CompetitionCredentialImportFailure.Forbidden);
        var credential = await db.CompetitionCredentials
            .Include(value => value.Batch)
            .Where(value => value.CompetitionId == request.CompetitionId && value.UserId == request.UserId)
            .OrderByDescending(value => value.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (credential?.Batch is null)
            return new(CompetitionCredentialImportFailure.CredentialNotFound);
        var user = await db.Users.SingleAsync(value => value.Id == request.UserId, ct);
        var password = GeneratePassword();
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        user.TokenVersion = checked(user.TokenVersion + 1);
        user.UpdatedAt = request.Now;
        credential.PasswordCiphertext = secrets.Protect(password, PlatformSecretPurpose.CompetitionCredentialPassword, credential.BatchId, user.Id);
        credential.PasswordVersion++;
        credential.PasswordUpdatedAt = request.Now;
        credential.UpdatedAt = request.Now;
        await db.SaveChangesAsync(ct);
        return new(CompetitionCredentialImportFailure.None, password);
    }

    private static CompetitionTrackDefinition? ResolveTrack(Competition competition, string? requestedKey)
    {
        var tracks = CompetitionTrackConfiguration.FromPersisted(competition.Mode, competition.Tracks);
        if (!competition.TracksEnabled)
            return tracks.DefaultTrack;
        var key = CompetitionTrackConfiguration.NormalizeKey(requestedKey);
        if (key is null)
            return null;
        var track = tracks.Find(key);
        return track is { IsInternal: false } ? track : null;
    }

    private static List<CompetitionCredentialImportIssue> ValidateRows(
        IReadOnlyList<CompetitionCredentialImportRow> rows,
        Competition competition)
    {
        var issues = new List<CompetitionCredentialImportIssue>();
        var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var teams = new Dictionary<string, (int Count, int Captains, string? TrackKey)>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var username = row.UserName.Trim();
            if (username.Length is < 3 or > 64 || !username.All(value => char.IsAsciiLetterOrDigit(value) || value is '_' or '-'))
                issues.Add(new(row.RowNumber, CompetitionCredentialImportIssueCode.MissingValue, "username"));
            if (!users.Add(username))
                issues.Add(new(row.RowNumber, CompetitionCredentialImportIssueCode.DuplicateUserName, username));
            if (!string.IsNullOrWhiteSpace(row.Email))
            {
                var email = EmailCanonicalizer.Canonicalize(row.Email);
                if (!new EmailAddressAttribute().IsValid(email))
                    issues.Add(new(row.RowNumber, CompetitionCredentialImportIssueCode.MissingValue, "email"));
                else if (!emails.Add(email))
                    issues.Add(new(row.RowNumber, CompetitionCredentialImportIssueCode.DuplicateEmail, email));
            }
            var name = row.TeamName.Trim();
            if (ResolveTrack(competition, row.TrackKey) is null)
                issues.Add(new(row.RowNumber, CompetitionCredentialImportIssueCode.InvalidTrackKey, row.TrackKey ?? string.Empty));
            teams.TryGetValue(name, out var summary);
            if (summary.Count > 0 && !string.Equals(summary.TrackKey, row.TrackKey, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(row.RowNumber, CompetitionCredentialImportIssueCode.InvalidTrackKey, name));
            teams[name] = (summary.Count + 1, summary.Captains + (row.Captain ? 1 : 0), row.TrackKey);
        }
        foreach (var team in teams)
        {
            if (team.Value.Captains != 1)
                issues.Add(new(0, CompetitionCredentialImportIssueCode.TeamCaptainConflict, team.Key));
            if (team.Value.Count > competition.MaxTeamMembers)
                issues.Add(new(0, CompetitionCredentialImportIssueCode.TeamMemberLimitExceeded, team.Key));
        }
        return issues;
    }

    private static CompetitionCredentialImportResult Failure(CompetitionCredentialImportFailure failure) =>
        new(null, failure, [], 0, 0, 0);

    private static string GeneratePassword()
    {
        Span<char> password = stackalloc char[16];
        for (var index = 0; index < password.Length; index++)
            password[index] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
        return new string(password);
    }

    private static string CreateInvitationToken()
    {
        Span<byte> bytes = stackalloc byte[20];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
