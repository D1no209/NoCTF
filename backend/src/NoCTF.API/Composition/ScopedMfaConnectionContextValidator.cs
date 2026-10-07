using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Authentication.Mfa;

namespace NoCTF.API.Composition;

// Singleton realtime routing needs a fresh scoped store for each batch.
public sealed class ScopedMfaConnectionContextValidator(IServiceScopeFactory scopes) : IMfaConnectionContextValidator
{
    public async Task<IReadOnlyDictionary<string, MfaFailure?>> ValidateAsync(
        IReadOnlyList<MfaContextValidationRequest> requests, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMfaAuthenticationStore>().ValidateContextsAsync(requests, ct);
    }
}
