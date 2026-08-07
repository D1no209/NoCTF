using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoCTF.API.Serialization;

public sealed class StrictPascalCaseEnumConverter<TEnum>()
    : JsonStringEnumConverter<TEnum>(namingPolicy: null, allowIntegerValues: false)
    where TEnum : struct, Enum;
