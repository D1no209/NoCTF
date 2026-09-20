using NoCTF.Bot.Persistence;

namespace NoCTF.Bot.Providers;

public sealed class OutboundMessageQueue(
    BotStateStore store,
    ChatProviderCatalog providers)
{
    public void Enqueue(
        string dedupeKey,
        string providerId,
        string groupId,
        string payload)
    {
        var parts = MessageText.Split(
            payload,
            providers.Active.Capabilities.MaximumTextLength);
        for (var index = 0; index < parts.Count; index++)
        {
            store.EnqueueOutbound(
                $"{dedupeKey}:part:{index}",
                providerId,
                groupId,
                parts[index]);
        }
    }
}

internal static class MessageText
{
    public static IReadOnlyList<string> Split(string value, int maximumLength)
    {
        var normalized = new string([.. value.Where(character =>
            character is '\n' or '\t' || !char.IsControl(character))]).Trim();
        if (normalized.Length <= maximumLength) return [normalized];
        var parts = new List<string>();
        var remaining = normalized;
        while (remaining.Length > maximumLength)
        {
            var split = remaining.LastIndexOf('\n', maximumLength - 1, maximumLength);
            if (split <= 0) split = maximumLength;
            if (split < remaining.Length
                && split > 0
                && char.IsHighSurrogate(remaining[split - 1])
                && char.IsLowSurrogate(remaining[split]))
            {
                split--;
            }
            parts.Add(remaining[..split].TrimEnd());
            remaining = remaining[split..].TrimStart('\n');
        }
        if (remaining.Length > 0) parts.Add(remaining);
        return parts;
    }
}
