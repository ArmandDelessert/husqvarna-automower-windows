using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HusqaCockpit.Core.Json;

/// <summary>
/// Reads and writes enums as UPPER_SNAKE_CASE strings ("PARKED_IN_CS" ⇄ ParkedInCs).
/// Unrecognized values become <c>default(TEnum)</c> rather than throwing, so a new
/// value introduced by Husqvarna does not break deserialization of the whole mower.
/// </summary>
public sealed class TolerantEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(TolerantEnumConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class TolerantEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        private static readonly ConcurrentDictionary<string, TEnum> s_byName = BuildLookup();

        private static ConcurrentDictionary<string, TEnum> BuildLookup()
        {
            var lookup = new ConcurrentDictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in Enum.GetValues<TEnum>())
            {
                lookup[ToWireName(value)] = value;
                lookup[value.ToString()] = value;
            }
            return lookup;
        }

        public static string ToWireName(TEnum value) => JsonNamingPolicy.SnakeCaseUpper.ConvertName(value.ToString());

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString();
                return text is not null && s_byName.TryGetValue(text, out var value) ? value : default;
            }
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), number);
            }
            reader.Skip();
            return default;
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            writer.WriteStringValue(ToWireName(value));
    }
}
