using System.Text.Json.Serialization;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Notifications;

namespace NoCTF.Application.Messaging;

[JsonSerializable(typeof(CompetitionEventCommitted))]
[JsonSerializable(typeof(GameplayFactStateChangedNotification))]
[JsonSerializable(typeof(ScoreboardUpdated))]
[JsonSerializable(typeof(NotificationChanged))]
[JsonSerializable(typeof(MfaAuthenticationChanged))]
[JsonSerializable(typeof(NoCTF.Application.LiveSolo.Realtime.LiveSoloMatchChanged))]
public partial class NoCtfMessageJsonContext : JsonSerializerContext;
