using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Challenges.Management;

public sealed record CreateCompetitionChallengeCommand(
    Guid? CompetitionChallengeId,
    Guid CompetitionId,
    Guid ChallengeId,
    long BaseScore,
    int Order,
    DateTimeOffset CreatedAt);

public sealed record UpdateCompetitionChallengeCommand(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    long BaseScore,
    int Order,
    bool IsPublished,
    int ExpectedRevision,
    DateTimeOffset UpdatedAt);

public sealed record ChallengeView(
    Guid Id,
    Guid CompetitionId,
    Guid ChallengeId,
    string Title,
    string? Description,
    string Direction,
    long BaseScore,
    int Order,
    bool IsPublished,
    int Revision,
    DateTimeOffset? DeletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum ChallengeMutationFailure
{
    InvalidChallengeId,
    InvalidBaseScore,
    InvalidOrder,
    InvalidRevision,
    CompetitionNotFound,
    TemplateNotFound,
    TemplateModeMismatch,
    ChallengeNotFound,
    ResourceIdConflict,
    ChallengeOrderConflict,
    ChallengeTemplateConflict,
    RevisionConflict,
    LifecycleStateConflict
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

public interface IChallengeManagementStore
{
    Task<ChallengeCompetitionContext?> GetCompetitionAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<ChallengeMutationResult> CreateAsync(
        CreateCompetitionChallengeCommand command,
        string configurationJson,
        CancellationToken cancellationToken);
    Task<ChallengeView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        bool includeUnpublished,
        bool includeDeleted,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ChallengeView>> ListAsync(
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
        int expectedRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<ChallengeMutationFailure?> RestoreAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        int expectedRevision,
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
        if (command.BaseScore < 0)
            return new(null, ChallengeMutationFailure.InvalidBaseScore);
        if (command.Order < 0)
            return new(null, ChallengeMutationFailure.InvalidOrder);

        var competition = await store.GetCompetitionAsync(command.CompetitionId, ct);
        if (competition is null)
            return new(null, ChallengeMutationFailure.CompetitionNotFound);

        return await store.CreateAsync(
            command,
            configurationCatalog.GetDefaultJson(competition.Mode),
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
    public Task<IReadOnlyList<ChallengeView>> ExecuteAsync(
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
        if (command.BaseScore < 0)
            return new(null, ChallengeMutationFailure.InvalidBaseScore);
        if (command.Order < 0)
            return new(null, ChallengeMutationFailure.InvalidOrder);
        if (command.ExpectedRevision < 0)
            return new(null, ChallengeMutationFailure.InvalidRevision);

        var result = await store.UpdateAsync(command, ct);
        if (result.Challenge is null)
            return result;

        return result;
    }
}

public sealed class DeleteChallenge(IChallengeManagementStore store)
{
    public Task<ChallengeMutationFailure?> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        int expectedRevision,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        ChangeAsync(
            competitionId,
            competitionChallengeId,
            expectedRevision,
            now,
            restore: false,
            ct);

    public Task<ChallengeMutationFailure?> RestoreAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        int expectedRevision,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        ChangeAsync(
            competitionId,
            competitionChallengeId,
            expectedRevision,
            now,
            restore: true,
            ct);

    private async Task<ChallengeMutationFailure?> ChangeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        int expectedRevision,
        DateTimeOffset now,
        bool restore,
        CancellationToken ct)
    {
        if (expectedRevision < 0)
            return ChallengeMutationFailure.InvalidRevision;

        var failure = restore
            ? await store.RestoreAsync(
                competitionId,
                competitionChallengeId,
                expectedRevision,
                now,
                ct)
            : await store.SoftDeleteAsync(
                competitionId,
                competitionChallengeId,
                expectedRevision,
                now,
                ct);
        if (failure is not null)
            return failure;

        return null;
    }
}
