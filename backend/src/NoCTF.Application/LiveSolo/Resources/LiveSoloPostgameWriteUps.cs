using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Domain.Challenges.WriteUps;

namespace NoCTF.Application.LiveSolo.Resources;

public interface ILiveSoloPostgameQuestionAccess
{
    Task<Guid?> ResolveAsync(LiveSoloResourceRequest request, CancellationToken ct);
    Task<IReadOnlyList<LiveSoloPostgameQuestion>?> ListAsync(Guid competitionId,Guid matchId,Guid actorId,CancellationToken ct);
}
public sealed record LiveSoloPostgameQuestion(Guid Id,Guid RoundId,Guid CompetitionChallengeId,int RoundNumber,int Position,string Title,DateTimeOffset OpenedAt);
public sealed class ManageLiveSoloWriteUps(ILiveSoloPostgameQuestionAccess access, ManageChallengeWriteUps writeUps)
{
    public async Task<WriteUpListView?> ListAsync(LiveSoloResourceRequest request, bool staff, CancellationToken ct)
        => await access.ResolveAsync(request,ct) is Guid challenge ? await writeUps.ListAsync(request.CompetitionId,challenge,request.ActorId,staff,request.Now,ct,request.QuestionId) : null;
    public async Task<WriteUpMutationResult> SaveMarkdownAsync(LiveSoloResourceRequest request,bool official,string markdown,Guid? expectedStamp,CancellationToken ct)
        => await access.ResolveAsync(request,ct) is Guid challenge ? await writeUps.SaveMarkdownAsync(new(request.CompetitionId,challenge,request.ActorId,official,
            WriteUpFormat.Markdown,markdown,null,expectedStamp,request.Now,request.QuestionId),ct) : new(Failure:ChallengeWriteUpFailure.Forbidden);
    public async Task<WriteUpMutationResult> SavePdfAsync(LiveSoloResourceRequest request,bool official,Guid? expectedStamp,string name,string contentType,long length,Stream content,CancellationToken ct)
        => await access.ResolveAsync(request,ct) is Guid challenge ? await writeUps.SavePdfAsync(new(request.CompetitionId,challenge,request.ActorId,official,
            WriteUpFormat.Pdf,null,null,expectedStamp,request.Now,request.QuestionId),name,contentType,length,content,ct) : new(Failure:ChallengeWriteUpFailure.Forbidden);
    public async Task<WriteUpMutationResult> SubmitAsync(LiveSoloResourceRequest request,bool official,Guid expectedStamp,CancellationToken ct)
        => await access.ResolveAsync(request,ct) is Guid challenge ? await writeUps.SubmitAsync(new(request.CompetitionId,challenge,request.ActorId,official,expectedStamp,request.Now,request.QuestionId),ct)
            : new(Failure:ChallengeWriteUpFailure.Forbidden);
    public async Task<WriteUpMutationResult> ReviewAsync(LiveSoloResourceRequest request,Guid writeUpId,Guid versionId,Guid expectedStamp,WriteUpReviewAction action,string? reason,CancellationToken ct)
        => await access.ResolveAsync(request,ct) is Guid challenge ? await writeUps.ReviewAsync(new(request.CompetitionId,challenge,writeUpId,versionId,request.ActorId,expectedStamp,action,reason,request.Now,request.QuestionId),ct)
            : new(Failure:ChallengeWriteUpFailure.Forbidden);
    public async Task<WriteUpContentView> ReadAsync(LiveSoloResourceRequest request,Guid versionId,bool staff,CancellationToken ct)
        => await access.ResolveAsync(request,ct) is Guid challenge ? await writeUps.ReadContentAsync(request.CompetitionId,challenge,versionId,request.ActorId,staff,request.Now,ct,request.QuestionId)
            : new(versionId,default,null,null,ChallengeWriteUpFailure.Forbidden);
    public async Task<WriteUpPdfContent?> OpenPdfAsync(LiveSoloResourceRequest request,Guid versionId,bool staff,CancellationToken ct)
    {
        if (await access.ResolveAsync(request,ct) is not Guid challenge) return null;
        var file=await writeUps.OpenPdfAsync(request.CompetitionId,challenge,versionId,request.ActorId,staff,request.Now,ct,request.QuestionId);
        if (file is not null && await access.ResolveAsync(request,ct) is null) { await file.Content.DisposeAsync(); return null; }
        return file;
    }
}
