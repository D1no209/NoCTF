using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoCTF.Bot.Providers.Milky;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MilkyGroupRole { Member, Admin, Owner }

public sealed record MilkyLoginInfo(long Uin, string Nickname);

public sealed record MilkyImplementationInfo(
    string ImplName,
    string ImplVersion,
    string MilkyVersion);

public sealed record MilkyGroupMember(
    long UserId,
    long GroupId,
    MilkyGroupRole Role);

public sealed record MilkyGroupMemberResponse(MilkyGroupMember Member);

public sealed record MilkySendResult(long MessageSeq, long Time);

public sealed record MilkyApiEnvelope<T>(
    string Status,
    int Retcode,
    T? Data,
    string? Message);

public sealed record MilkyEventEnvelope(
    long Time,
    long SelfId,
    string EventType,
    JsonElement Data);

public sealed class MilkyApiException(int retcode, string message)
    : Exception($"Milky request failed with retcode {retcode}: {message}")
{
    public int Retcode { get; } = retcode;
}
