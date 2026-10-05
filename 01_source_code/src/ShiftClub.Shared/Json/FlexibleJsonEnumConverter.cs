using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShiftClub.Shared.Json;

/// <summary>
/// Writes enums as numbers (compatible with Shell 0.5.0).
/// Reads both numbers and string names ("UpdateClient", "Lock").
/// </summary>
public sealed class FlexibleJsonEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        var t = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        return t.IsEnum;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var underlying = Nullable.GetUnderlyingType(typeToConvert);
        if (underlying is not null)
        {
            var nullableConverter = typeof(NullableEnumConverter<>).MakeGenericType(underlying);
            return (JsonConverter)Activator.CreateInstance(nullableConverter)!;
        }

        var converter = typeof(EnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converter)!;
    }

    private sealed class EnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ReadEnum(ref reader);

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(Convert.ToInt32(value));

        internal static TEnum ReadEnum(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetInt32(out var i))
                    return (TEnum)Enum.ToObject(typeof(TEnum), i);
                if (reader.TryGetInt64(out var l))
                    return (TEnum)Enum.ToObject(typeof(TEnum), l);
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                    throw new JsonException($"Empty enum for {typeof(TEnum).Name}");

                if (int.TryParse(s, out var asInt))
                    return (TEnum)Enum.ToObject(typeof(TEnum), asInt);

                if (Enum.TryParse<TEnum>(s, ignoreCase: true, out var named))
                    return named;
            }

            throw new JsonException($"Cannot convert {reader.TokenType} to {typeof(TEnum).Name}");
        }
    }

    private sealed class NullableEnumConverter<TEnum> : JsonConverter<TEnum?>
        where TEnum : struct, Enum
    {
        public override TEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;
            return EnumConverter<TEnum>.ReadEnum(ref reader);
        }

        public override void Write(Utf8JsonWriter writer, TEnum? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteNumberValue(Convert.ToInt32(value.Value));
        }
    }
}
