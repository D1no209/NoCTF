using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using Microsoft.Extensions.Logging;

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
    CompetitionNotAcceptingQuestions,
    TeamNotEligible,
    InvalidChallengeReference,
    SubmissionNotFound,
    TeamActiveQuestionLimitReached,
    ParticipantMessageLimitReached,
    RevisionConflict,
    InvalidTransition,
    QuestionClosed,
    EntryNotFound
}

public enum CompetitionQuestionChallengeReferenceState : short
{
    Missing,
    Valid,
    Invalid
}

public sealed record CompetitionQuestionCreationContext(
    bool IsHuman,
    CompetitionStatus CompetitionStatus,
    bool HasApprovedTeam,
    CompetitionQuestionSubject Subject,
    CompetitionQuestionChallengeReferenceState ChallengeReference,
    bool HasValidSubmission,
    int ActiveQuestionCount,
    int MaxActiveQuestionsPerTeam);

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
    string? ChallengeTitle,
    string Title,
    string Body,
    CompetitionQuestionStatus Status,
    CompetitionQuestionAccess Access,
    string LastActorDisplayName,
    CompetitionQuestionParticipantRole LastActorRole,
    int ParticipantMessagesRemaining,
    int MaxParticipantMessagesBeforeHandlerReply,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CompetitionQuestionEntryView> Entries);

public sealed record CompetitionQuestionMutationResult(
    CompetitionQuestionView? Question,
    CompetitionQuestionFailure? Failure = null,
    int? Limit = null);

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
            return CompetitionQuestionFailure.CompetitionNotAcceptingQuestions;
        if (!context.HasApprovedTeam)
            return CompetitionQuestionFailure.TeamNotEligible;
        if (context.Subject == CompetitionQuestionSubject.Challenge
            && context.ChallengeReference != CompetitionQuestionChallengeReferenceState.Valid)
            return CompetitionQuestionFailure.InvalidChallengeReference;
        if (context.Subject == CompetitionQuestionSubject.Platform
            && context.ChallengeReference != CompetitionQuestionChallengeReferenceState.Missing)
            return CompetitionQuestionFailure.InvalidChallengeReference;
        if (!context.HasValidSubmission)
            return CompetitionQuestionFailure.SubmissionNotFound;
        if (context.ActiveQuestionCount >= context.MaxActiveQuestionsPerTeam)
            return CompetitionQuestionFailure.TeamActiveQuestionLimitReached;
        return null;
    }

    public static CompetitionQuestionFailure? ValidateParticipantMessageLimit(
        int participantMessagesSinceHandlerReply,
        int maximumMessages) =>
        participantMessagesSinceHandlerReply >= maximumMessages
            ? CompetitionQuestionFailure.ParticipantMessageLimitReached
            : null;

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
        (current, IsHandler(actor), IsParticipant(actor)) switch
        {
            (CompetitionQuestionStatus.Closed, _, _) => null,
            (CompetitionQuestionStatus.Pending or CompetitionQuestionStatus.Replied,
                true, _) => CompetitionQuestionStatus.Replied,
            (CompetitionQuestionStatus.Replied or CompetitionQuestionStatus.Resolved,
                _, true) => CompetitionQuestionStatus.Pending,
            (CompetitionQuestionStatus.Pending, _, true) =>
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
        if (IsParticipant(actor))
        {
            return current == CompetitionQuestionStatus.Replied
                && target == CompetitionQuestionStatus.Resolved;
        }
        if (IsHandler(actor))
        {
            return target switch
            {
                CompetitionQuestionStatus.Resolved =>
                    current is CompetitionQuestionStatus.Pending or CompetitionQuestionStatus.Replied,
                CompetitionQuestionStatus.Closed => true,
                _ => false
            };
        }
        return false;
    }

    public static bool IsParticipant(CompetitionQuestionParticipantRole role) =>
        role is CompetitionQuestionParticipantRole.Asker
            or CompetitionQuestionParticipantRole.Participant;

    public static bool IsHandler(CompetitionQuestionParticipantRole role) =>
        role is CompetitionQuestionParticipantRole.Handler
            or CompetitionQuestionParticipantRole.Judge
            or CompetitionQuestionParticipantRole.ChallengeOwner
            or CompetitionQuestionParticipantRole.CompetitionManager
            or CompetitionQuestionParticipantRole.PlatformAdministrator;

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

public sealed class CreateCompetitionQuestion(
    ICompetitionQuestionStore store,
    ILogger<CreateCompetitionQuestion> logger)
{
    public Task<CompetitionQuestionMutationResult> ExecuteAsync(
        CreateCompetitionQuestionCommand command,
        CancellationToken ct = default)
    {
        if (!Enum.IsDefined(command.Subject)
            || command.CompetitionId == Guid.Empty
            || command.ActorUserId == Guid.Empty)
        {
            LogFailure(CompetitionQuestionFailure.InvalidRequest, command);
            return Task.FromResult(new CompetitionQuestionMutationResult(
                null,
                CompetitionQuestionFailure.InvalidRequest));
        }
        var textFailure = CompetitionQuestionRules.ValidateText(command.Title, command.Body);
        if (textFailure is not null)
        {
            LogFailure(textFailure.Value, command);
            return Task.FromResult(new CompetitionQuestionMutationResult(null, textFailure));
        }
        return store.CreateAsync(command with
        {
            Title = command.Title.Trim(),
            Body = command.Body.Trim()
        }, ct);
    }

    private void LogFailure(
        CompetitionQuestionFailure failure,
        CreateCompetitionQuestionCommand command) =>
        logger.LogWarning(
            "Competition question mutation rejected. failureCode={FailureCode} competitionId={CompetitionId} questionId={QuestionId} teamId={TeamId} userId={UserId}",
            failure,
            command.CompetitionId,
            null,
            null,
            command.ActorUserId);
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

public sealed class AddCompetitionQuestionMessage(
    ICompetitionQuestionStore store,
    ILogger<AddCompetitionQuestionMessage> logger)
{
    public Task<CompetitionQuestionMutationResult> ExecuteAsync(
        AddCompetitionQuestionMessageCommand command,
        CancellationToken ct = default)
    {
        if (command.ExpectedRevision < 0)
        {
            LogFailure(CompetitionQuestionFailure.InvalidRequest, command);
            return Task.FromResult(new CompetitionQuestionMutationResult(
                null,
                CompetitionQuestionFailure.InvalidRequest));
        }
        var failure = CompetitionQuestionRules.ValidateMessage(command.Body);
        if (failure is not null)
        {
            LogFailure(failure.Value, command);
            return Task.FromResult(new CompetitionQuestionMutationResult(null, failure));
        }
        return store.AddMessageAsync(command with { Body = command.Body.Trim() }, ct);
    }

    private void LogFailure(
        CompetitionQuestionFailure failure,
        AddCompetitionQuestionMessageCommand command) =>
        logger.LogWarning(
            "Competition question mutation rejected. failureCode={FailureCode} competitionId={CompetitionId} questionId={QuestionId} teamId={TeamId} userId={UserId}",
            failure,
            command.CompetitionId,
            command.QuestionId,
            null,
            command.ActorUserId);
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
