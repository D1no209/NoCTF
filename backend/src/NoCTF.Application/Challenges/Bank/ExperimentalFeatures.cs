using System.Text.Json;
using NoCTF.Domain.Challenges;

namespace NoCTF.Application.Challenges.Bank;

public interface IExperimentalFeatureReader
{
    Task<bool> IsCtfPatchVerificationEnabledAsync(CancellationToken cancellationToken);
}

internal static class CtfInteractionDefinition
{
    public static CtfInteractionKind Parse(string definitionJson)
    {
        try
        {
            using var document = JsonDocument.Parse(definitionJson);
            if (!document.RootElement.TryGetProperty("interactionKind", out var value))
                return CtfInteractionKind.FlagSubmission;
            return value.ValueKind == JsonValueKind.Number
                && value.TryGetInt16(out var numeric)
                && Enum.IsDefined((CtfInteractionKind)numeric)
                    ? (CtfInteractionKind)numeric
                    : CtfInteractionKind.FlagSubmission;
        }
        catch (JsonException)
        {
            return CtfInteractionKind.FlagSubmission;
        }
    }
}
