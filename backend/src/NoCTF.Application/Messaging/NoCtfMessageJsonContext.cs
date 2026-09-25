using System.Text.Json.Serialization;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Notifications;

namespace NoCTF.Application.Messaging;

[JsonSerializable(typeof(CompetitionEventCommitted))]
[JsonSerializable(typeof(GameplayFactStateChangedNotification))]
[JsonSerializable(typeof(ScoreboardUpdated))]
public partial class NoCtfMessageJsonContext : JsonSerializerContext;
