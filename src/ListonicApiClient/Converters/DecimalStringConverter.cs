using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ListonicApiClient.Converters;

public partial class DecimalStringConverter : JsonConverter<decimal?>
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => ParseDecimalString(reader.GetString()),
            JsonTokenType.Number => reader.GetDecimal(),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for decimal.")
        };

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture));
    }

    public static decimal? ParseDecimalString(string? raw)
    {
        if (raw is null) return null;

        // Strip all Unicode whitespace (regular space, narrow no-break space U+202F, etc.)
        var s = WhitespaceRegex().Replace(raw, "");

        // Determine the decimal separator by which separator appears last.
        // "1.234,56" -> last is comma -> comma is decimal, dots are thousands
        // "1,234.56" -> last is dot   -> dot is decimal, commas are thousands
        // "0,5"      -> last is comma -> comma is decimal
        // "1.234"    -> last is dot   -> dot is decimal (lone dot treated as decimal)
        var lastDot = s.LastIndexOf('.');
        var lastComma = s.LastIndexOf(',');

        string normalized;
        if (lastComma > lastDot)
        {
            // Comma is decimal separator -- strip dots, replace comma with dot
            normalized = s.Replace(".", "").Replace(',', '.');
        }
        else
        {
            // Dot is decimal separator (or no separators) -- strip commas
            normalized = s.Replace(",", "");
        }

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
