using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication.Privacy;

public sealed class AccountPrivacyStore(NoCtfDbContext db, IOptions<AccountPrivacyOptions> options,
    TimeProvider clock, IRequestSourceAddress? source = null) : IAccountPrivacyStore, IAccountActivityRecorder
{
    public Task<SchoolIdentity?> GetOwnAsync(Guid userId, CancellationToken ct) => db.Users.AsNoTracking()
        .Where(user => user.Id == userId && user.AccountStatus == UserAccountStatus.Active)
        .Select(user => new SchoolIdentity(user.SchoolFullName, user.SchoolStudentNumber)).SingleOrDefaultAsync(ct);

    public async Task<bool> SaveOwnAsync(Guid userId, SchoolIdentity identity, CancellationToken ct) =>
        await db.Users.Where(user => user.Id == userId && user.AccountStatus == UserAccountStatus.Active)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.SchoolFullName, identity.FullName)
                .SetProperty(user => user.SchoolStudentNumber, identity.StudentNumber)
                .SetProperty(user => user.UpdatedAt, clock.GetUtcNow()), ct) == 1;

    public Task<bool> IsAdministratorAsync(Guid actorId, CancellationToken ct) => db.Users.AnyAsync(user =>
        user.Id == actorId && user.Kind == UserKind.Human && user.Role == UserRole.Administrator
        && user.AccountStatus == UserAccountStatus.Active, ct);

    public Task<bool> IsTeamMemberAsync(Guid competitionId, Guid teamId, Guid memberId, CancellationToken ct) =>
        db.Teams.AnyAsync(team => team.Id == teamId && team.CompetitionId == competitionId
            && team.DeletedAt == null && team.RegistrationStatus != NoCTF.Domain.Teams.TeamRegistrationStatus.Rejected
            && team.MemberIds.Contains(memberId), ct);

    public async Task<PrivateAccountDetails?> ReadAsync(Guid userId, Guid? competitionId, CancellationToken ct)
    {
        var identity = await db.Users.AsNoTracking().Where(user => user.Id == userId)
            .Select(user => new SchoolIdentity(user.SchoolFullName, user.SchoolStudentNumber)).SingleOrDefaultAsync(ct);
        if (identity is null) return null;
        var cutoff = clock.GetUtcNow().AddDays(-options.Value.IpRetentionDays);
        var facts = await db.GameplayFacts.AsNoTracking().Where(fact => fact.ActorUserId == userId
                && (competitionId == null || fact.CompetitionId == competitionId)
                && fact.OccurredAt >= cutoff
                && (fact.Kind == GameplayFactKind.FlagAttempt || fact.Kind == GameplayFactKind.BreakAttempt
                    || fact.Kind == GameplayFactKind.FixAttempt))
            .OrderByDescending(fact => fact.OccurredAt).ThenByDescending(fact => fact.Id).Take(50)
            .Select(fact => new AccountActivity(fact.Id,
                fact.Kind == GameplayFactKind.FixAttempt ? AccountActivityKind.PatchUploaded : AccountActivityKind.FlagSubmitted,
                fact.OccurredAt, fact.SourceIpAddress, fact.CompetitionId, fact.Id)).ToListAsync(ct);
        // Competition personnel must never receive platform authentication history.
        if (competitionId is null)
        {
            var logins = await db.Notifications.AsNoTracking().Where(item =>
                    item.Kind == NotificationKind.AuthenticationSecurityActivity && item.SourceId == userId
                    && item.SentAt >= cutoff).OrderByDescending(item => item.SentAt).ThenByDescending(item => item.Id)
                .Take(50).ToListAsync(ct);
            facts.AddRange(logins.Select(item => JsonSerializer.Deserialize<AccountActivity>(item.ContentJson)!)
                .Where(item => item is not null));
        }
        return new(identity, facts.OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Id).Take(50).ToArray(),
            options.Value.IpRetentionDays);
    }

    public async Task RecordLoginAsync(Guid? authenticatedUserId, DateTimeOffset now, CancellationToken ct)
    {
        db.Notifications.Add(AuthenticationActivity.Create(authenticatedUserId,
            authenticatedUserId is null ? AccountActivityKind.LoginFailed : AccountActivityKind.LoggedIn, source?.Address, now));
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveExpiredAddressesAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddDays(-options.Value.IpRetentionDays);
        // Retention deletes only security events, never ordinary notifications or gameplay facts.
        await db.Notifications.Where(item => item.Kind == NotificationKind.AuthenticationSecurityActivity && item.SentAt < cutoff)
            .ExecuteDeleteAsync(ct);
        await db.GameplayFacts.Where(fact => fact.SourceIpAddress != null && fact.OccurredAt < cutoff)
            .ExecuteUpdateAsync(setters => setters.SetProperty(fact => fact.SourceIpAddress, (string?)null), ct);
    }
}

internal static class AuthenticationActivity
{
    internal static Notification Create(Guid? userId, AccountActivityKind kind, string? address, DateTimeOffset now)
    {
        var id = Guid.CreateVersion7(now);
        return new Notification
        {
            Id = id, SourceType = userId is null ? NotificationSourceType.System : NotificationSourceType.User,
            SourceId = userId, TargetType = NotificationTargetType.PlatformAdministrators,
            TargetId = Notification.PlatformAdministratorsTargetId,
            Kind = NotificationKind.AuthenticationSecurityActivity, SentAt = now,
            ContentJson = JsonSerializer.Serialize(new AccountActivity(id, kind, now, address))
        };
    }
}
