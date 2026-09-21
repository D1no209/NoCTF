namespace NoCTF.Application.Authentication.Privacy;

using NoCTF.Domain.Identity;

public interface IRequestSourceAddress
{
    string? Address { get; }
}

public enum AccountActivityKind
{
    Registered,
    LoggedIn,
    LoginFailed,
    FlagSubmitted,
    PatchUploaded,
    SsoLoggedIn,
    SsoLoginFailed
}

public sealed record AccountActivity(Guid Id, AccountActivityKind Kind, DateTimeOffset OccurredAt,
    string? IpAddress, Guid? CompetitionId = null, Guid? GameplayFactId = null,
    Guid? SsoProviderId = null)
{
    [System.Text.Json.Serialization.JsonPropertyName("schemaVersion")]
    public int SchemaVersion => 1;
    public override string ToString() => $"AccountActivity {{ Id = {Id}, Kind = {Kind}, IpAddress = [REDACTED] }}";
}
public sealed record SchoolIdentity(string? FullName, string? StudentNumber);
public sealed record PrivateSsoBinding(
    Guid ProviderId,
    string? ProviderName,
    string? ProviderIconUrl,
    SsoProtocol Protocol,
    string Subject,
    DateTimeOffset BoundAt);
public sealed record PrivateAccountDetails(
    SchoolIdentity Identity,
    IReadOnlyList<AccountActivity> Activities,
    int RetentionDays,
    PrivateSsoBinding? SsoBinding = null);

public interface IAccountActivityRecorder
{
    Task RecordLoginAsync(Guid? authenticatedUserId, DateTimeOffset now, CancellationToken ct);
    Task RecordSsoAsync(
        Guid? authenticatedUserId,
        Guid providerId,
        bool succeeded,
        DateTimeOffset now,
        CancellationToken ct);
}

public interface IAccountPrivacyStore
{
    Task<SchoolIdentity?> GetOwnAsync(Guid userId, CancellationToken ct);
    Task<bool> SaveOwnAsync(Guid userId, SchoolIdentity identity, CancellationToken ct);
    Task<bool> IsAdministratorAsync(Guid actorId, CancellationToken ct);
    Task<bool> IsTeamMemberAsync(Guid competitionId, Guid teamId, Guid memberId, CancellationToken ct);
    Task<PrivateAccountDetails?> ReadAsync(Guid userId, Guid? competitionId, CancellationToken ct);
}

public sealed class AccountPrivacy(IAccountPrivacyStore store,
    NoCTF.Application.Teams.Moderation.ICompetitionModerationAuthorizer authorizer)
{
    public Task<SchoolIdentity?> GetOwnAsync(Guid userId, CancellationToken ct) => store.GetOwnAsync(userId, ct);
    public Task<bool> SaveOwnAsync(Guid userId, SchoolIdentity value, CancellationToken ct)
    {
        var normalized = new SchoolIdentity(Normalize(value.FullName), Normalize(value.StudentNumber));
        if (normalized.FullName?.Length > 100 || normalized.StudentNumber?.Length > 64
            || normalized.FullName?.Any(char.IsControl) == true || normalized.StudentNumber?.Any(char.IsControl) == true)
            throw new ArgumentException("姓名最多 100 字符、学号最多 64 字符，且不能包含控制字符。");
        return store.SaveOwnAsync(userId, normalized, ct);
    }
    public async Task<PrivateAccountDetails?> ReadPlatformAsync(Guid actorId, Guid userId, CancellationToken ct) =>
        await store.IsAdministratorAsync(actorId, ct) ? await store.ReadAsync(userId, null, ct) : null;
    public async Task<PrivateAccountDetails?> ReadMemberAsync(Guid actorId, Guid competitionId,
        Guid teamId, Guid memberId, CancellationToken ct) =>
        await authorizer.CanJudgeAsync(actorId, competitionId, ct)
        && await store.IsTeamMemberAsync(competitionId, teamId, memberId, ct)
            ? await store.ReadAsync(memberId, competitionId, ct) : null;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class AccountPrivacyOptions
{
    public int IpRetentionDays { get; set; } = 30;
}
