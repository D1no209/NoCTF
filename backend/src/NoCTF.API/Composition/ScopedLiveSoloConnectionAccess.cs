using NoCTF.API.LiveSolo.Realtime;
using NoCTF.Application.LiveSolo.Realtime;

namespace NoCTF.API.Composition;

// Connects the singleton transport guard to a fresh scoped, batched authorization store.
public sealed class ScopedLiveSoloConnectionAccess(IServiceScopeFactory scopes) : ILiveSoloConnectionAccess
{
    public async Task<IReadOnlySet<string>> EligibleAsync(IReadOnlyList<LiveSoloRealtimeAccessRequest> requests, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILiveSoloRealtimeAccess>().EligibleAsync(requests, ct);
    }
}
