using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed class LiveSoloMediaScheduleSource(NoCtfDbContext db) : IClusterScheduleContributor
{
    public async Task<IReadOnlyList<ClusterScheduleEntry>> RebuildAsync(DateTimeOffset now, CancellationToken ct) =>
        (await db.LiveSoloMediaSessions.AsNoTracking().Where(x => x.State != LiveSoloMediaState.Stopped).Select(x => x.Id).ToArrayAsync(ct))
            .Select(id => new ClusterScheduleEntry($"live-solo-media:{id:N}", ClusterScheduleKind.LiveSoloMedia, now, TimeSpan.FromSeconds(5),
                new RefreshLiveSoloMedia(id))).ToArray();
}
