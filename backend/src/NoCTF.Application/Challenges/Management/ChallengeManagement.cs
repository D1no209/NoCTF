using NoCTF.Application.Common;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
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
    CompetitionNotFound,
    TemplateNotFound,
    TemplateModeMismatch,
    ChallengeNotFound,
    ResourceIdConflict,
    ChallengeOrderConflict,
    RevisionConflict
}

public sealed record ChallengeMutationResult(
    ChallengeView? Challenge,
    ChallengeMutationFailure? Failure = null);

public sealed record ChallengeCompetitionContext(GameMode Mode, CompetitionStatus Status);

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
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<ChallengeMutationFailure?> RestoreAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

internal static class ChallengeMutationFailureProtocol
{
    public static string Code(ChallengeMutationFailure failure) => failure switch
    {
        ChallengeMutationFailure.CompetitionNotFound => "competition_not_found",
        ChallengeMutationFailure.TemplateNotFound => "challenge_template_not_found",
        ChallengeMutationFailure.TemplateModeMismatch => "challenge_template_mode_mismatch",
        ChallengeMutationFailure.ChallengeNotFound => "competition_challenge_not_found",
        ChallengeMutationFailure.ResourceIdConflict => "resource_id_conflict",
        ChallengeMutationFailure.ChallengeOrderConflict => "challenge_order_conflict",
        ChallengeMutationFailure.RevisionConflict => "revision_conflict",
        _ => "challenge_conflict"
    };
}

public sealed class CreateChallenge(
    IChallengeManagementStore store,
    IChallengeConfigurationCatalog configurationCatalog)
{
    public async Task<OperationResult<ChallengeView>> ExecuteAsync(
        CreateCompetitionChallengeCommand command,
        CancellationToken ct = default)
    {
        if (command.ChallengeId == Guid.Empty)
            return OperationResult<ChallengeView>.Failure("invalid_challenge_id", "ChallengeId is required.");
        if (command.BaseScore < 0)
            return OperationResult<ChallengeView>.Failure("invalid_base_score", "BaseScore cannot be negative.");
        if (command.Order < 0)
            return OperationResult<ChallengeView>.Failure("invalid_order", "Order cannot be negative.");

        var competition = await store.GetCompetitionAsync(command.CompetitionId, ct);
        if (competition is null)
            return OperationResult<ChallengeView>.Failure("competition_not_found", "Competition was not found.");

        var result = await store.CreateAsync(
            command,
            configurationCatalog.GetDefaultJson(competition.Mode),
            ct);
        return result.Challenge is not null
            ? OperationResult<ChallengeView>.Success(result.Challenge)
            : Failure(result.Failure);
    }

    private static OperationResult<ChallengeView> Failure(ChallengeMutationFailure? failure)
    {
        var actual = failure ?? ChallengeMutationFailure.ChallengeOrderConflict;
        return OperationResult<ChallengeView>.Failure(
            ChallengeMutationFailureProtocol.Code(actual),
            "Competition challenge was not created.");
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

public sealed class UpdateChallenge(
    IChallengeManagementStore store,
    ILeaderboardCache cache,
    IBackendMessagePublisher messages)
{
    public async Task<OperationResult<ChallengeView>> ExecuteAsync(
        UpdateCompetitionChallengeCommand command,
        CancellationToken ct = default)
    {
        if (command.BaseScore < 0 || command.Order < 0 || command.ExpectedRevision < 0)
            return OperationResult<ChallengeView>.Failure(
                "invalid_competition_challenge",
                "BaseScore, Order, and ExpectedRevision cannot be negative.");

        var result = await store.UpdateAsync(command, ct);
        if (result.Challenge is null)
        {
            var failure = result.Failure ?? ChallengeMutationFailure.RevisionConflict;
            return OperationResult<ChallengeView>.Failure(
                ChallengeMutationFailureProtocol.Code(failure),
                "Competition challenge was not updated.");
        }

        await cache.InvalidateAsync(command.CompetitionId, ct);
        await messages.RebuildCompetitionAsync(command.CompetitionId, ct);
        return OperationResult<ChallengeView>.Success(result.Challenge);
    }
}

public sealed class DeleteChallenge(
    IChallengeManagementStore store,
    ILeaderboardCache cache,
    IBackendMessagePublisher messages)
{
    public Task<OperationResult> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        ChangeAsync(competitionId, competitionChallengeId, now, restore: false, ct);

    public Task<OperationResult> RestoreAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        ChangeAsync(competitionId, competitionChallengeId, now, restore: true, ct);

    private async Task<OperationResult> ChangeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        bool restore,
        CancellationToken ct)
    {
        var failure = restore
            ? await store.RestoreAsync(competitionId, competitionChallengeId, now, ct)
            : await store.SoftDeleteAsync(competitionId, competitionChallengeId, now, ct);
        if (failure is not null)
            return OperationResult.Failure(
                ChallengeMutationFailureProtocol.Code(failure.Value),
                restore ? "Competition challenge was not restored." : "Competition challenge was not deleted.");

        await cache.InvalidateAsync(competitionId, ct);
        await messages.RebuildCompetitionAsync(competitionId, ct);
        return OperationResult.Success();
    }
}
