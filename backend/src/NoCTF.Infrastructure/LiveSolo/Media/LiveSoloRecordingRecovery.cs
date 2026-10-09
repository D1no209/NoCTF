using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed class LiveSoloRecordingRecovery(NoCtfDbContext db,ICompetitionModerationAuthorizer authorizer,
    ILiveSoloCaptureStore captures,ILiveSoloRecordingStore recordings):ILiveSoloRecordingRecovery
{
    public async Task<LiveSoloRecordingChangeResult> RecoverAsync(ChangeLiveSoloRecording command,CancellationToken ct)
    {
        if(!await db.Users.AnyAsync(x=>x.Id==command.ActorId&&x.AccountStatus==UserAccountStatus.Active,ct)
            || !await authorizer.CanModerateAsync(command.ActorId,command.CompetitionId,ct))return new(null,LiveSoloFailure.Forbidden);
        var failure=await captures.RecoverRecordingAsync(command,ct);
        if(failure is not null)return new(null,failure);
        var record=await recordings.ReadRecordingAsync(command.CompetitionId,command.MatchId,command.RecordingId,command.ActorId,ct);
        return new(record,record is null?LiveSoloFailure.NotFound:null);
    }
}
