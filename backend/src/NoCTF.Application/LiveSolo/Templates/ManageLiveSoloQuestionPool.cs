using NoCTF.Domain.Challenges;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Templates;

public enum LiveSoloPoolFailure : short
{
    NotFound, Forbidden, Conflict, UnsupportedDefinition, ActiveRound, SourceChanged,
    ProtectedMaterialsForbidden, InvalidContent, AlreadyInQuestionBank
}

public sealed record LiveSoloPoolQuestion(Guid Id, Guid ChallengeId, string Title, string? Description, string Direction,
    Guid ConcurrencyStamp, Guid MaterialStamp, LiveSoloMaterialOwnership Ownership, Guid? SourceTemplateId,
    LiveSoloChallengeDefinition Definition, IReadOnlyList<Guid> QuestionGroupIds)
{
    public bool CanEditContent { get; init; } = true;
    public IReadOnlyList<ChallengeAttachmentView> Attachments { get; init; } = [];
    public IReadOnlyList<ChallengeFlagView> Flags { get; init; } = [];
}
public interface ILiveSoloPoolOperation { LiveSoloPoolFailure? Failure { get; } }
public sealed record LiveSoloPoolResult(LiveSoloPoolQuestion? Question, LiveSoloPoolFailure? Failure = null) : ILiveSoloPoolOperation;
public sealed record CreateLiveSoloPoolQuestion(Guid CompetitionId, Guid ActorId, string Title, string? Description,
    string Direction, LiveSoloChallengeDefinition Definition, DateTimeOffset Now);
public sealed record DeployLiveSoloPoolQuestion(Guid CompetitionId, Guid ActorId, Guid SourceTemplateId, Guid ExpectedSourceStamp, DateTimeOffset Now);
public sealed record EditLiveSoloPoolQuestion(Guid CompetitionId, Guid CompetitionChallengeId, Guid ActorId,
    Guid ExpectedStamp, Guid ExpectedMaterialStamp, string Title, string? Description, string Direction,
    LiveSoloChallengeDefinition Definition, DateTimeOffset Now,
    IReadOnlyList<LiveSoloPoolFlagInput>? Flags = null, IReadOnlyList<Guid>? RemovedAttachmentIds = null,
    IReadOnlyList<LiveSoloPoolUploadedAttachment>? AddedAttachments = null);
public sealed record LiveSoloPoolFlagInput(string Flag, ChallengeFlagMatchKind MatchKind);
public sealed record LiveSoloPoolUploadedAttachment(Guid FileId, string? ExactFlag = null);
public sealed record ExportLiveSoloPoolQuestion(Guid CompetitionId, Guid CompetitionChallengeId, Guid ActorId,
    Guid ExpectedStamp, Guid ExpectedMaterialStamp, DateTimeOffset Now);
public sealed record LiveSoloPoolExport(Guid? ChallengeId, LiveSoloPoolFailure? Failure = null) : ILiveSoloPoolOperation;

public interface ILiveSoloQuestionPoolStore
{
    Task<IReadOnlyList<LiveSoloPoolQuestion>?> ListAsync(Guid competitionId, Guid actorId, CancellationToken ct);
    Task<LiveSoloPoolFailure?> CheckEditAsync(Guid competitionId, Guid entryId, Guid actorId, Guid expectedStamp, Guid materialStamp, CancellationToken ct);
    Task<LiveSoloPoolResult> CreateAsync(CreateLiveSoloPoolQuestion command, CancellationToken ct);
    Task<LiveSoloPoolResult> DeployAsync(DeployLiveSoloPoolQuestion command, CancellationToken ct);
    Task<LiveSoloPoolResult> EditAsync(EditLiveSoloPoolQuestion command, CancellationToken ct);
    Task<LiveSoloPoolExport> ExportAsync(ExportLiveSoloPoolQuestion command, CancellationToken ct);
}

public sealed class ManageLiveSoloQuestionPool(ILiveSoloQuestionPoolStore store)
{
    public Task<IReadOnlyList<LiveSoloPoolQuestion>?> ListAsync(Guid competitionId, Guid actorId, CancellationToken ct) => store.ListAsync(competitionId, actorId, ct);
    public Task<LiveSoloPoolResult> CreateAsync(CreateLiveSoloPoolQuestion command, CancellationToken ct) => store.CreateAsync(command, ct);
    public Task<LiveSoloPoolResult> DeployAsync(DeployLiveSoloPoolQuestion command, CancellationToken ct) => store.DeployAsync(command, ct);
    public Task<LiveSoloPoolResult> EditAsync(EditLiveSoloPoolQuestion command, CancellationToken ct) => store.EditAsync(command, ct);
    public Task<LiveSoloPoolExport> ExportAsync(ExportLiveSoloPoolQuestion command, CancellationToken ct) => store.ExportAsync(command, ct);
}
