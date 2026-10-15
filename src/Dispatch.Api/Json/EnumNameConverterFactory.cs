using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dispatch.Api.Json;

/// <summary>
/// Enums travel as their names. Reading accepts only a defined name (case-insensitive) and rejects
/// everything else, including numbers and numeric strings. <see cref="JsonStringEnumConverter"/> is
/// not enough: even with <c>allowIntegerValues: false</c> it accepts <c>"1"</c> as the enum's value 1.
/// </summary>
internal sealed class EnumNameConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(EnumNameConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class EnumNameConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var name = reader.GetString();
                foreach (var value in Enum.GetValues<TEnum>())
                {
                    if (string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return value;
                    }
                }
            }

            throw new JsonException($"Expected one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
