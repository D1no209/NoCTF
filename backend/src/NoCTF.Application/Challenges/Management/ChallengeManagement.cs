using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;

namespace NoCTF.Application.Challenges.Management;

public sealed record CreateCompetitionChallengeCommand(
    Guid? CompetitionChallengeId,
    Guid CompetitionId,
    Guid ChallengeId,
    int Order,
    DateTimeOffset CreatedAt,
    string? CustomTitle = null,
    IReadOnlyList<string>? Tags = null,
    ChallengeTiming? Timing = null);

public sealed record UpdateCompetitionChallengeCommand(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    int Order,
    bool IsPublished,
    DateTimeOffset UpdatedAt,
    string? CustomTitle = null,
    Guid? DirectionId = null,
    IReadOnlyList<string>? Tags = null);

public sealed record ChallengeView(
    Guid Id,
    Guid CompetitionId,
    Guid ChallengeId,
    string Title,
    string? CustomTitle,
    string? Description,
    string Direction,
    int Order,
    bool IsPublished,
    DateTimeOffset? DeletedAt,
    bool HasRuntime,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public ChallengeTiming Timing { get; init; } = new();
    public ChallengeOpeningState OpeningState { get; init; }
    public bool TimingRecalculationPending { get; init; }
    public Guid? DirectionId { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? DirectionIcon { get; init; }
    public bool UsesDynamicFlag { get; init; }
    public CtfInteractionKind InteractionKind { get; init; }
}

public sealed record CompetitionChallengeSummaryView(
    Guid Id,
    Guid CompetitionId,
    Guid ChallengeId,
    string Title,
    string? CustomTitle,
    string Direction,
    int Order,
    bool IsPublished,
    DateTimeOffset? DeletedAt,
    CtfInteractionKind InteractionKind,
    Guid? DirectionId = null,
    string? DirectionIcon = null)
{
    public ChallengeTiming Timing { get; init; } = new();
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public enum ChallengeMutationFailure
{
    InvalidChallengeId,
    InvalidTitle,
    InvalidOrder,
    InvalidDirection,
    InvalidTags,
    CompetitionNotFound,
    TemplateNotFound,
    TemplateModeMismatch,
    ChallengeNotFound,
    ResourceIdConflict,
    ChallengeOrderConflict,
    ChallengeTemplateConflict,
    LifecycleStateConflict,
    ExperimentalFeatureDisabled,
    InvalidTiming,
    TimingUnsupported
}

public sealed record ChallengeMutationResult(
    ChallengeView? Challenge,
    ChallengeMutationFailure? Failure = null);

public sealed record ChallengeCompetitionContext(GameMode Mode, CompetitionStatus Status);

public static class ParticipantChallengeVisibilityPolicy
{
    public static bool CanView(CompetitionStatus status) =>
        status is CompetitionStatus.Running
            or CompetitionStatus.Paused
            or CompetitionStatus.Finished;
}

public sealed record CompetitionChallengeReadDecision(
    CompetitionVisibilityAccessDecision Visibility,
    Guid? TeamId);

public interface ICompetitionChallengeReadAccess
{
    Task<CompetitionChallengeReadDecision?> ResolveAsync(
        Guid userId,
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IChallengeManagementStore
{
    Task<ChallengeCompetitionContext?> GetCompetitionAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<ChallengeMutationResult> CreateAsync(
        CreateCompetitionChallengeCommand command,
        CompetitionChallengeRules rules,
        CancellationToken cancellationToken);
    Task<ChallengeView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        bool includeUnpublished,
        bool includeDeleted,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<CompetitionChallengeSummaryView>> ListAsync(
        Guid competitionId,
        bool includeUnpublished,
        bool includeDeleted,
        CancellationToken cancellationToken);
    Task<ChallengeMutationResult> UpdateAsync(
        UpdateCompetitionChallengeCommand command,
        CancellationToken cancellationToken);
    Task<ChallengeMutationFailure?> SoftDeleteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<ChallengeMutationFailure?> RestoreAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class CreateChallenge(
    IChallengeManagementStore store,
    IChallengeConfigurationCatalog configurationCatalog)
{
    public async Task<ChallengeMutationResult> ExecuteAsync(
        CreateCompetitionChallengeCommand command,
        CancellationToken ct = default)
    {
        if (command.ChallengeId == Guid.Empty)
            return new(null, ChallengeMutationFailure.InvalidChallengeId);
        var customTitle = CompetitionChallengeTitle.Normalize(command.CustomTitle);
        if (customTitle is { Length: > CompetitionChallengeTitle.MaximumLength })
            return new(null, ChallengeMutationFailure.InvalidTitle);
        if (command.Order < 0)
            return new(null, ChallengeMutationFailure.InvalidOrder);
        if (!CompetitionChallengeTags.TryNormalize(command.Tags, out var tags))
            return new(null, ChallengeMutationFailure.InvalidTags);

        var competition = await store.GetCompetitionAsync(command.CompetitionId, ct);
        if (competition is null)
            return new(null, ChallengeMutationFailure.CompetitionNotFound);
        if (command.Timing is { IsValid: false }) return new(null, ChallengeMutationFailure.InvalidTiming);
        if (competition.Mode == GameMode.LiveSolo && command.Timing is { IsEmpty: false })
            return new(null, ChallengeMutationFailure.TimingUnsupported);

        return await store.CreateAsync(
            command with { CustomTitle = customTitle, Tags = tags },
            configurationCatalog.CreateDefaultRules(
                competition.Mode,
                command.CompetitionChallengeId ?? Guid.Empty),
            ct);
    }
}

public sealed class GetChallenge(IChallengeManagementStore store)
{
    public Task<ChallengeView?> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        bool includeUnpublished,
        bool includeDeleted = false,
        CancellationToken ct = default) =>
        store.FindAsync(
            competitionId,
            competitionChallengeId,
            includeUnpublished,
            includeDeleted,
            ct);
}

public sealed class ListChallenges(IChallengeManagementStore store)
{
    public Task<IReadOnlyList<CompetitionChallengeSummaryView>> ExecuteAsync(
        Guid competitionId,
        bool includeUnpublished,
        bool includeDeleted = false,
        CancellationToken ct = default) =>
        store.ListAsync(competitionId, includeUnpublished, includeDeleted, ct);
}

public sealed class UpdateChallenge(IChallengeManagementStore store)
{
    public async Task<ChallengeMutationResult> ExecuteAsync(
        UpdateCompetitionChallengeCommand command,
        CancellationToken ct = default)
    {
        var customTitle = CompetitionChallengeTitle.Normalize(command.CustomTitle);
        if (customTitle is { Length: > CompetitionChallengeTitle.MaximumLength })
            return new(null, ChallengeMutationFailure.InvalidTitle);
        if (command.Order < 0)
            return new(null, ChallengeMutationFailure.InvalidOrder);
        if (!CompetitionChallengeTags.TryNormalize(command.Tags, out var tags))
            return new(null, ChallengeMutationFailure.InvalidTags);
        var result = await store.UpdateAsync(command with
        {
            CustomTitle = customTitle,
            Tags = command.Tags is null ? null : tags
        }, ct);
        if (result.Challenge is null)
            return result;

        return result;
    }
}

internal static class CompetitionChallengeTitle
{
    public const int MaximumLength = 160;

    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class DeleteChallenge(IChallengeManagementStore store)
{
    public Task<ChallengeMutationFailure?> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        ChangeAsync(
            competitionId,
            competitionChallengeId,
            now,
            restore: false,
            ct);

    public Task<ChallengeMutationFailure?> RestoreAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        ChangeAsync(
            competitionId,
            competitionChallengeId,
            now,
            restore: true,
            ct);

    private async Task<ChallengeMutationFailure?> ChangeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        bool restore,
        CancellationToken ct)
    {
        var failure = restore
            ? await store.RestoreAsync(
                competitionId,
                competitionChallengeId,
                now,
                ct)
            : await store.SoftDeleteAsync(
                competitionId,
                competitionChallengeId,
                now,
                ct);
        if (failure is not null)
            return failure;

        return null;
    }
}
