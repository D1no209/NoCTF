using System.Text.Json;
using System.Text.Json.Serialization;
using NoCTF.Application.Administration.PlatformLogs;

namespace NoCTF.Application.Messaging;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(PlatformLogView))]
public partial class NoCtfWebMessageJsonContext : JsonSerializerContext;
