using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeUsageWidget.Core;

/// Enums as their NAMES, and a name this build does not know (written by a
/// newer build, or mistyped by hand) reads as the type's default instead of
/// failing the whole file: one field falls back, the accounts survive.
/// Ordinals are still read, as JsonStringEnumConverter read them.
internal sealed class TolerantEnumConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert))!;

    private sealed class Converter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.String when Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var named)
                                          && Enum.IsDefined(named) => named,
                JsonTokenType.Number when reader.TryGetInt32(out var ordinal)
                                          && Enum.IsDefined(typeof(T), ordinal) => (T)Enum.ToObject(typeof(T), ordinal),
                JsonTokenType.String or JsonTokenType.Number => default,
                _ => throw new JsonException($"Expected a {typeof(T).Name} name, got {reader.TokenType}."),
            };

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
