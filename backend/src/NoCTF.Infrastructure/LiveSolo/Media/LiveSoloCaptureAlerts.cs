using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Realtime;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed partial class LiveSoloCaptureStore
{
    private async Task CaptureAlertAsync(LiveSoloMediaSession session,Guid exportId,Guid revision,LiveSoloMediaAlertKind reason,CancellationToken ct)
    {
        var key=$"live-solo-export:{exportId:N}:{revision:N}:{(short)reason}";
        if(await db.Notifications.AnyAsync(x=>x.Kind==NotificationKind.LiveSoloMediaInterrupted&&x.SourceEventKey==key,ct))return;
        var match=await db.LiveSoloMatches.SingleAsync(x=>x.Id==session.MatchId,ct);var now=clock.GetUtcNow();
        var alert=NotificationGeneratedCatalog.Create(NotificationKind.LiveSoloMediaInterrupted);
        alert.Id=Guid.CreateVersion7(now);alert.SourceType=NotificationSourceType.System;alert.SourceId=exportId;
        alert.TargetType=NotificationTargetType.CompetitionCollaborators;alert.TargetId=match.CompetitionId;
        alert.RelatedType=EntityReferenceKind.Competition;alert.RelatedId=match.CompetitionId;alert.CompetitionId=match.CompetitionId;
        alert.LiveSoloMatchId=match.Id;alert.LiveSoloMediaSessionId=session.Id;alert.LiveSoloMediaAlertKind=reason;
        alert.SentAt=now;alert.PayloadOccurredAt=now;alert.SourceEventKey=key;
        db.Notifications.Add(alert);await messages.PublishAsync(new LiveSoloMediaAlertCreated(alert.Id));
        await messages.PublishAsync(new LiveSoloMatchChanged(match.CompetitionId,match.Id,now));
    }
    private async Task CheckProgramProgressAsync(LiveSoloMediaSession session,LiveSoloProgramCapture program,CancellationToken ct)
    {
        await TransactionAsync(async()=>{
            await db.Entry(program).ReloadAsync(ct);
            var latest=program.LastFragmentImportedAt??program.StartedAt;
            if(program.State!=LiveSoloCaptureState.Active || program.StalledAt!=null || latest is null
                || clock.GetUtcNow()-latest<TimeSpan.FromSeconds(options.ProgramStallSeconds))return;
            program.StalledAt=clock.GetUtcNow();
            await CaptureAlertAsync(session,program.Id,program.ConcurrencyStamp,LiveSoloMediaAlertKind.ProgramStalled,ct);
        },ct);
    }
    private Task RecordingFailureAsync(LiveSoloMediaSession session,LiveSoloRecording record,LiveSoloRecordingState state,
        LiveSoloRecordingFailure failure,CancellationToken ct,LiveSoloExportObservation? observation=null)=>TransactionAsync(async()=>{
            await db.Entry(record).ReloadAsync(ct);
            if(record.State==state&&record.Failure==failure)return;
            if(observation is not null)
            {record.EgressId=observation.Id;record.StartedAt??=observation.StartedAt;record.EndedAt=observation.EndedAt;}
            record.State=state;record.Failure=failure;
            await CaptureAlertAsync(session,record.Id,record.ConcurrencyStamp,LiveSoloMediaAlertKind.RecordingFailed,ct);
        },ct);
}
