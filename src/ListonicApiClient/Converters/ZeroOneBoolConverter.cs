using System.Text.Json;
using System.Text.Json.Serialization;

namespace ListonicApiClient.Converters;

public class ZeroOneBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
            throw new JsonException($"Expected a number (0 or 1), got {reader.TokenType}.");
        return reader.GetInt32() != 0;
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value ? 1 : 0);
}
