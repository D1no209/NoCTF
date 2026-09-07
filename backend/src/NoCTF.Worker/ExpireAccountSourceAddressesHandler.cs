using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Authentication.Privacy;

namespace NoCTF.Worker;

public sealed class ExpireAccountSourceAddressesHandler(AccountPrivacyStore store)
{
    public Task Handle(ExpireAccountSourceAddresses message, CancellationToken ct) => store.RemoveExpiredAddressesAsync(ct);
}
