using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Challenges.Questions;

public enum CompetitionQuestionAccess : short
{
    Asker,
    Observer,
    Handler
}

public enum CompetitionQuestionFailure : short
{
    InvalidRequest,
    SpamRejected,
    NotFound,
    Forbidden,
    LifecycleConflict,
    TeamNotEligible,
    ChallengeNotFound,
    SubmissionNotFound,
    RevisionConflict,
    InvalidTransition,
    QuestionClosed,
    EntryNotFound
}

public sealed record CompetitionQuestionCreationContext(
    bool IsHuman,
    CompetitionStatus CompetitionStatus,
    bool HasApprovedTeam,
    CompetitionQuestionSubject Subject,
    bool HasValidChallenge,
    bool HasValidSubmission);

public sealed record CompetitionQuestionEntryView(
    Guid Id,
    CompetitionQuestionEntryKind Kind,
    CompetitionQuestionParticipantRole ActorRole,
    Guid? ActorUserId,
    string ActorDisplayName,
    string? Body,
    CompetitionQuestionStatus? FromStatus,
    CompetitionQuestionStatus? ToStatus,
    Guid? TargetEntryId,
    DateTimeOffset CreatedAt);

public sealed record CompetitionQuestionView(
    Guid Id,
    Guid CompetitionId,
    Guid? CompetitionChallengeId,
    Guid? TeamId,
    Guid? AskedByUserId,
    string AskerDisplayName,
    string? TeamDisplayName,
    Guid? SubmissionId,
    CompetitionQuestionSubject Subject,
    string Title,
    string Body,
    CompetitionQuestionStatus Status,
    CompetitionQuestionAccess Access,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CompetitionQuestionEntryView> Entries);

public sealed record CompetitionQuestionMutationResult(
    CompetitionQuestionView? Question,
    CompetitionQuestionFailure? Failure = null);

public sealed record CreateCompetitionQuestionCommand(
    Guid CompetitionId,
    CompetitionQuestionSubject Subject,
    Guid? CompetitionChallengeId,
    Guid? SubmissionId,
    Guid ActorUserId,
    string Title,
    string Body,
    DateTimeOffset Now);

public sealed record AddCompetitionQuestionMessageCommand(
    Guid CompetitionId,
    Guid QuestionId,
    Guid ActorUserId,
    string Body,
    int ExpectedRevision,
    DateTimeOffset Now);

public sealed record ChangeCompetitionQuestionStatusCommand(
    Guid CompetitionId,
    Guid QuestionId,
    Guid ActorUserId,
    CompetitionQuestionStatus Status,
    int ExpectedRevision,
    DateTimeOffset Now);

public sealed record CompetitionQuestionQuery(
    Guid CompetitionId,
    Guid ActorUserId,
    Guid? CompetitionChallengeId,
    CompetitionQuestionSubject? Subject,
    CompetitionQuestionStatus? Status,
    int Limit);

public interface ICompetitionQuestionStore
{
    Task<CompetitionQuestionMutationResult> CreateAsync(
        CreateCompetitionQuestionCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CompetitionQuestionView>> ListAsync(
        CompetitionQuestionQuery query,
        CancellationToken cancellationToken);

    Task<CompetitionQuestionView?> FindAsync(
        Guid competitionId,
        Guid questionId,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task<CompetitionQuestionMutationResult> AddMessageAsync(
        AddCompetitionQuestionMessageCommand command,
        CancellationToken cancellationToken);

    Task<CompetitionQuestionMutationResult> ChangeStatusAsync(
        ChangeCompetitionQuestionStatusCommand command,
        CancellationToken cancellationToken);

}

public static class CompetitionQuestionRules
{
    public const int MinimumTitleLength = 4;
    public const int MaximumTitleLength = 160;
    public const int MinimumBodyLength = 4;
    public const int MaximumBodyLength = 4000;
    public const int MaximumListLimit = 100;

    public static CompetitionQuestionFailure? ValidateCreation(
        CompetitionQuestionCreationContext context)
    {
        if (!context.IsHuman)
            return CompetitionQuestionFailure.Forbidden;
        if (context.CompetitionStatus is not (CompetitionStatus.Running or CompetitionStatus.Paused))
            return CompetitionQuestionFailure.LifecycleConflict;
        if (!context.HasApprovedTeam)
            return CompetitionQuestionFailure.TeamNotEligible;
        if (context.Subject == CompetitionQuestionSubject.Challenge && !context.HasValidChallenge)
            return CompetitionQuestionFailure.ChallengeNotFound;
        if (context.Subject == CompetitionQuestionSubject.Platform && context.HasValidChallenge)
            return CompetitionQuestionFailure.InvalidRequest;
        if (!context.HasValidSubmission)
            return CompetitionQuestionFailure.SubmissionNotFound;
        return null;
    }

    public static CompetitionQuestionFailure? ValidateText(string title, string body)
    {
        var normalizedTitle = title.Trim();
        var normalizedBody = body.Trim();
        if (normalizedTitle.Length is < MinimumTitleLength or > MaximumTitleLength
            || normalizedBody.Length is < MinimumBodyLength or > MaximumBodyLength
            || normalizedTitle.Any(IsUnsupportedControl)
            || normalizedBody.Any(IsUnsupportedControl))
            return CompetitionQuestionFailure.InvalidRequest;
        return LooksLikeSpam(normalizedBody)
            ? CompetitionQuestionFailure.SpamRejected
            : null;
    }

    public static CompetitionQuestionFailure? ValidateMessage(string body)
    {
        var normalized = body.Trim();
        if (normalized.Length is < MinimumBodyLength or > MaximumBodyLength
            || normalized.Any(IsUnsupportedControl))
            return CompetitionQuestionFailure.InvalidRequest;
        return LooksLikeSpam(normalized)
            ? CompetitionQuestionFailure.SpamRejected
            : null;
    }

    public static CompetitionQuestionStatus? StatusAfterMessage(
        CompetitionQuestionStatus current,
        CompetitionQuestionParticipantRole actor) =>
        (current, actor) switch
        {
            (CompetitionQuestionStatus.Closed, _) => null,
            (CompetitionQuestionStatus.Pending or CompetitionQuestionStatus.Replied,
                CompetitionQuestionParticipantRole.Handler) => CompetitionQuestionStatus.Replied,
            (CompetitionQuestionStatus.Replied or CompetitionQuestionStatus.Resolved,
                CompetitionQuestionParticipantRole.Asker) => CompetitionQuestionStatus.Pending,
            (CompetitionQuestionStatus.Pending, CompetitionQuestionParticipantRole.Asker) =>
                CompetitionQuestionStatus.Pending,
            _ => null
        };

    public static bool CanTransition(
        CompetitionQuestionStatus current,
        CompetitionQuestionStatus target,
        CompetitionQuestionParticipantRole actor)
    {
        if (current == target || current == CompetitionQuestionStatus.Closed)
            return false;
        return actor switch
        {
            CompetitionQuestionParticipantRole.Asker =>
                current == CompetitionQuestionStatus.Replied
                && target == CompetitionQuestionStatus.Resolved,
            CompetitionQuestionParticipantRole.Handler => target switch
            {
                CompetitionQuestionStatus.Resolved =>
                    current is CompetitionQuestionStatus.Pending or CompetitionQuestionStatus.Replied,
                CompetitionQuestionStatus.Closed => true,
                _ => false
            },
            _ => false
        };
    }

    public static int NormalizeLimit(int limit) => Math.Clamp(limit, 1, MaximumListLimit);

    private static bool IsUnsupportedControl(char value) =>
        char.IsControl(value) && value is not ('\r' or '\n' or '\t');

    private static bool LooksLikeSpam(string value)
    {
        if (value.Length < 24)
            return false;
        var nonWhitespace = value.Where(character => !char.IsWhiteSpace(character)).ToArray();
        if (nonWhitespace.Length == 0)
            return true;
        var largestGroup = nonWhitespace
            .GroupBy(character => char.ToUpperInvariant(character))
            .Max(group => group.Count());
        return largestGroup >= Math.Ceiling(nonWhitespace.Length * 0.85);
    }
}

public sealed class CreateCompetitionQuestion(ICompetitionQuestionStore store)
{
    public Task<CompetitionQuestionMutationResult> ExecuteAsync(
        CreateCompetitionQuestionCommand command,
        CancellationToken ct = default)
    {
        if (!Enum.IsDefined(command.Subject)
            || command.CompetitionId == Guid.Empty
            || command.ActorUserId == Guid.Empty)
            return Task.FromResult(new CompetitionQuestionMutationResult(
                null,
                CompetitionQuestionFailure.InvalidRequest));
        var textFailure = CompetitionQuestionRules.ValidateText(command.Title, command.Body);
        if (textFailure is not null)
            return Task.FromResult(new CompetitionQuestionMutationResult(null, textFailure));
        return store.CreateAsync(command with
        {
            Title = command.Title.Trim(),
            Body = command.Body.Trim()
        }, ct);
    }
}

public sealed class ListCompetitionQuestions(ICompetitionQuestionStore store)
{
    public Task<IReadOnlyList<CompetitionQuestionView>> ExecuteAsync(
        CompetitionQuestionQuery query,
        CancellationToken ct = default) =>
        store.ListAsync(query with
        {
            Limit = CompetitionQuestionRules.NormalizeLimit(query.Limit)
        }, ct);
}

public sealed class GetCompetitionQuestion(ICompetitionQuestionStore store)
{
    public Task<CompetitionQuestionView?> ExecuteAsync(
        Guid competitionId,
        Guid questionId,
        Guid actorUserId,
        CancellationToken ct = default) =>
        store.FindAsync(competitionId, questionId, actorUserId, ct);
}

public sealed class AddCompetitionQuestionMessage(ICompetitionQuestionStore store)
{
    public Task<CompetitionQuestionMutationResult> ExecuteAsync(
        AddCompetitionQuestionMessageCommand command,
        CancellationToken ct = default)
    {
        if (command.ExpectedRevision < 0)
            return Task.FromResult(new CompetitionQuestionMutationResult(
                null,
                CompetitionQuestionFailure.InvalidRequest));
        var failure = CompetitionQuestionRules.ValidateMessage(command.Body);
        if (failure is not null)
            return Task.FromResult(new CompetitionQuestionMutationResult(null, failure));
        return store.AddMessageAsync(command with { Body = command.Body.Trim() }, ct);
    }
}

public sealed class ChangeCompetitionQuestionStatus(ICompetitionQuestionStore store)
{
    public Task<CompetitionQuestionMutationResult> ExecuteAsync(
        ChangeCompetitionQuestionStatusCommand command,
        CancellationToken ct = default) =>
        command.ExpectedRevision < 0 || !Enum.IsDefined(command.Status)
            ? Task.FromResult(new CompetitionQuestionMutationResult(
                null,
                CompetitionQuestionFailure.InvalidRequest))
            : store.ChangeStatusAsync(command, ct);
}
