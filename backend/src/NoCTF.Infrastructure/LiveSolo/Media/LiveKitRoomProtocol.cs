using System.Text.Json.Serialization;

namespace NoCTF.Infrastructure.LiveSolo.Media;

internal enum LiveKitRoomOperation { CreateRoom, ListRooms, ListParticipants, DeleteRoom, RemoveParticipant }
[JsonConverter(typeof(JsonStringEnumConverter<LiveKitParticipantState>))]
internal enum LiveKitParticipantState { JOINING, JOINED, ACTIVE, DISCONNECTED }
[JsonConverter(typeof(JsonStringEnumConverter<LiveKitTrackType>))]
internal enum LiveKitTrackType { AUDIO, VIDEO, DATA }
[JsonConverter(typeof(JsonStringEnumConverter<LiveKitTrackSource>))]
internal enum LiveKitTrackSource { UNKNOWN, CAMERA, MICROPHONE, SCREEN_SHARE, SCREEN_SHARE_AUDIO }
internal sealed record LiveKitRoomIdentity(string Room);
internal sealed record LiveKitParticipantIdentity(string Room, string Identity);
internal sealed record LiveKitCreateRoom(string Name, int EmptyTimeout, int DepartureTimeout, int MaxParticipants);
internal sealed record LiveKitListRooms(string[] Names);
internal sealed record LiveKitRooms(LiveKitRoom[]? Rooms);
internal sealed record LiveKitRoom(string Name);
internal sealed record LiveKitParticipants(LiveKitParticipant[]? Participants);
internal sealed record LiveKitParticipant(string Identity, LiveKitParticipantState State, LiveKitTrack[]? Tracks);
internal sealed record LiveKitTrack(string Sid, LiveKitTrackType Type, LiveKitTrackSource Source, bool Muted);
internal sealed record LiveKitVideoGrant(
    [property: JsonPropertyName("room")] string Room,
    [property: JsonPropertyName("roomJoin")] bool RoomJoin = false,
    [property: JsonPropertyName("roomCreate")] bool RoomCreate = false,
    [property: JsonPropertyName("roomList")] bool RoomList = false,
    [property: JsonPropertyName("roomAdmin")] bool RoomAdmin = false,
    [property: JsonPropertyName("canPublish")] bool CanPublish = false,
    [property: JsonPropertyName("canSubscribe")] bool CanSubscribe = false,
    [property: JsonPropertyName("canPublishData")] bool CanPublishData = false,
    [property: JsonPropertyName("canUpdateOwnMetadata")] bool CanUpdateOwnMetadata = false,
    [property: JsonPropertyName("canPublishSources")] string[]? CanPublishSources = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(LiveKitRoomIdentity))]
[JsonSerializable(typeof(LiveKitParticipantIdentity))]
[JsonSerializable(typeof(LiveKitCreateRoom))]
[JsonSerializable(typeof(LiveKitListRooms))]
[JsonSerializable(typeof(LiveKitRooms))]
[JsonSerializable(typeof(LiveKitParticipants))]
[JsonSerializable(typeof(LiveKitVideoGrant))]
internal partial class LiveKitJsonContext : JsonSerializerContext;
