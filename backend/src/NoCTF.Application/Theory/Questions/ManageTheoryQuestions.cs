using NoCTF.Domain.Theory.Questions;

namespace NoCTF.Application.Theory.Questions;

public sealed record TheoryOptionInput(string Content, bool IsCorrect, TheoryTruthValue? TruthValue = null);
public sealed record TheoryClassificationInput(TheoryClassificationKind Kind, string Name);
public sealed record TheoryQuestionInput(string Title, string Body, string? Explanation,
    TheoryQuestionKind Kind, TheoryQuestionDifficulty Difficulty, IReadOnlyList<TheoryOptionInput> Options,
    IReadOnlyList<TheoryClassificationInput> Classifications, IReadOnlyList<Guid> FileIds);
public sealed record SaveTheoryQuestion(Guid ActorId, Guid? QuestionId, TheoryQuestionInput Content, DateTimeOffset Now);
public sealed record TheoryQuestionSaved(Guid QuestionId, Guid RevisionId, int Version, Guid SourceId);
public enum TheoryBankFailure { NotFoundOrForbidden, CreationForbidden, InvalidContent, Conflict, InvalidPermission, FileNotAuthorized }
public sealed record TheoryBankWriteResult(TheoryQuestionSaved? Saved = null, TheoryBankFailure? Failure = null,
    TheoryQuestionFailure? Validation = null);
public sealed record TheoryQuestionSummary(Guid Id, Guid OwnerId, Guid SourceId, int Version, string Title,
    TheoryQuestionKind Kind, TheoryQuestionDifficulty Difficulty, TheoryQuestionState State, DateTimeOffset UpdatedAt,
    bool CanReadAnswers, bool CanManage)
{
    public DateTimeOffset? FirstExposedAt { get; init; }
    public int PaperReferences { get; init; }
    public IReadOnlyList<TheoryClassificationInput> Classifications { get; init; } = [];
}
public sealed record TheoryQuestionPage(IReadOnlyList<TheoryQuestionSummary> Items, int Total);
public sealed record TheoryQuestionContent(TheoryQuestionSummary Question, Guid RevisionId, TheoryQuestionInput Content)
{
    public IReadOnlyList<TheoryQuestionAccessGrant> Grants { get; init; } = [];
}
public sealed record TheoryQuestionAccessGrant(Guid UserId, string Name, TheoryQuestionPermission Permission);

public sealed record TheoryQuestionFilter(TheoryQuestionState? State, TheoryQuestionDifficulty? Difficulty, string? Knowledge, string? Tag);
public interface ITheoryQuestionBank
{
    Task<TheoryBankWriteResult> SaveAsync(Guid actorId, Guid? questionId, TheoryQuestionRevision revision, CancellationToken ct);
    Task<TheoryBankWriteResult> CopyAsync(Guid actorId, Guid questionId, DateTimeOffset now, CancellationToken ct);
    Task<TheoryQuestionPage> ListAsync(Guid actorId, string? search, TheoryQuestionKind? kind, int offset, int limit, CancellationToken ct, TheoryQuestionFilter? filters = null);
    Task<TheoryQuestionContent?> ReadAsync(Guid actorId, Guid questionId, CancellationToken ct);
    Task<TheoryBankFailure?> SetStateAsync(Guid actorId, Guid questionId, TheoryQuestionState state, DateTimeOffset now, CancellationToken ct);
    Task<TheoryBankFailure?> GrantAsync(Guid actorId, Guid questionId, Guid userId, TheoryQuestionPermission? permission, CancellationToken ct);
}

public sealed class ManageTheoryQuestions(ITheoryQuestionBank bank)
{
    public Task<TheoryBankWriteResult> SaveAsync(SaveTheoryQuestion command, CancellationToken ct = default)
    {
        var content = command.Content;
        var revision = new TheoryQuestionRevision
        {
            Id = Guid.CreateVersion7(command.Now), Title = content.Title.Trim(), Body = content.Body,
            Explanation = content.Explanation, Kind = content.Kind, Difficulty = content.Difficulty, CreatedAt = command.Now,
            Options = content.Options.Select((option, index) => new TheoryQuestionOption
            {
                Id = Guid.NewGuid(), Order = index, Content = option.Content, IsCorrect = option.IsCorrect, TruthValue = option.TruthValue
            }).ToList(),
            Classifications = content.Classifications.Select(item => new TheoryQuestionClassification
            { Kind = item.Kind, Name = item.Name.Trim(), NormalizedName = TheoryQuestionPolicy.Normalize(item.Name) }).ToList(),
            Files = content.FileIds.Select(id => new TheoryQuestionFile { FileId = id }).ToList()
        };
        var invalid = TheoryQuestionPolicy.Validate(revision);
        return invalid is { } failure
            ? Task.FromResult(new TheoryBankWriteResult(Failure: TheoryBankFailure.InvalidContent, Validation: failure))
            : bank.SaveAsync(command.ActorId, command.QuestionId, revision, ct);
    }

    public Task<TheoryBankFailure?> SetStateAsync(Guid actorId, Guid questionId, TheoryQuestionState state,
        DateTimeOffset now, CancellationToken ct = default) => Enum.IsDefined(state)
        ? bank.SetStateAsync(actorId, questionId, state, now, ct)
        : Task.FromResult<TheoryBankFailure?>(TheoryBankFailure.InvalidContent);

    public Task<TheoryBankFailure?> GrantAsync(Guid actorId, Guid questionId, Guid userId,
        TheoryQuestionPermission? permission, CancellationToken ct = default) => permission is { } value && !Enum.IsDefined(value)
        ? Task.FromResult<TheoryBankFailure?>(TheoryBankFailure.InvalidPermission)
        : bank.GrantAsync(actorId, questionId, userId, permission, ct);
}
