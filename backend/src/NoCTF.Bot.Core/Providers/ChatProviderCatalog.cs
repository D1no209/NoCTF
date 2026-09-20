using Microsoft.Extensions.Options;
using NoCTF.Bot.Configuration;

namespace NoCTF.Bot.Providers;

public sealed class ChatProviderCatalog
{
    public ChatProviderCatalog(
        IEnumerable<IChatProvider> providers,
        IOptions<RelayOptions> options)
    {
        var providerById = new Dictionary<string, IChatProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Id))
                throw new InvalidOperationException("A chat provider has no identifier.");
            if (!providerById.TryAdd(provider.Id, provider))
                throw new InvalidOperationException($"Duplicate chat provider identifier '{provider.Id}'.");
        }

        if (!providerById.TryGetValue(options.Value.Provider, out var selected))
        {
            throw new InvalidOperationException(
                $"Configured chat provider '{options.Value.Provider}' is not registered.");
        }
        if (!selected.Capabilities.SupportsGroupMessages
            || !selected.Capabilities.SupportsGroupMemberRoles
            || selected.Capabilities.MaximumTextLength < 256)
        {
            throw new InvalidOperationException(
                $"Chat provider '{selected.Id}' does not satisfy the required group capabilities.");
        }
        if (!selected.TryNormalizeUserId(options.Value.MasterUserId, out var masterUserId))
        {
            throw new InvalidOperationException(
                "Bot:MasterUserId is not valid for the selected chat provider.");
        }

        Active = selected;
        MasterUserId = masterUserId;
    }

    public IChatProvider Active { get; }

    public string MasterUserId { get; }
}
