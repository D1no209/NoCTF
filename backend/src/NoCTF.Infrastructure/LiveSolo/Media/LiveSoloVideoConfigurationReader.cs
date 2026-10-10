using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Media;

internal static class LiveSoloVideoConfigurationReader
{
    internal static async Task<LiveSoloVideoPolicy> ReadAsync(NoCtfDbContext db,CancellationToken ct)=>
        await db.PlatformSettings.AsNoTracking().Where(x=>x.Id==1).Select(x=>new LiveSoloVideoPolicy(x.LiveSoloVideoMaximumWidth,x.LiveSoloVideoMaximumHeight,
            x.LiveSoloVideoMaximumFramesPerSecond,x.LiveSoloVideoMaximumBitrateBitsPerSecond,x.LiveSoloVideoMaximumBitrateBitsPerSecond,x.LiveSoloVideoPolicyStamp)).SingleAsync(ct);
}
