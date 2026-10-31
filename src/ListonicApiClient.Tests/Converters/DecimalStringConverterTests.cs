using System.Text.Json;
using System.Text.Json.Serialization;
using ListonicApiClient.Converters;

namespace ListonicApiClient.Tests.Converters;

public class DecimalStringConverterTests
{
    private record Model(
        [property: JsonConverter(typeof(DecimalStringConverter))] decimal? Value);

    private static decimal? Deserialize(string json) =>
        JsonSerializer.Deserialize<Model>(json)!.Value;

    private static string Serialize(decimal? value) =>
        JsonSerializer.Serialize(new Model(value));

    // Write

    [Test]
    public async Task Write_WholeNumber_WritesQuotedString()
    {
        await Assert.That(Serialize(13m)).IsEqualTo("""{"Value":"13"}""");
    }

    [Test]
    public async Task Write_DotDecimal_WritesQuotedString()
    {
        await Assert.That(Serialize(0.5m)).IsEqualTo("""{"Value":"0.5"}""");
    }

    [Test]
    public async Task Write_Null_WritesNull()
    {
        await Assert.That(Serialize(null)).IsEqualTo("""{"Value":null}""");
    }

    [Test]
    public async Task Write_UsesInvariantCulture()
    {
        await Assert.That(Serialize(1.25m)).IsEqualTo("""{"Value":"1.25"}""");
    }

    // Read — string token (via JSON)

    [Test]
    public async Task Read_QuotedDotDecimal_ReturnsDecimal()
    {
        await Assert.That(Deserialize("""{"Value":"13.4"}""")).IsEqualTo(13.4m);
    }

    [Test]
    public async Task Read_QuotedWholeNumber_ReturnsDecimal()
    {
        await Assert.That(Deserialize("""{"Value":"500"}""")).IsEqualTo(500m);
    }

    [Test]
    public async Task Read_QuotedUnparseable_ReturnsNull()
    {
        await Assert.That(Deserialize("""{"Value":"abc"}""")).IsNull();
    }

    // Read — number token

    [Test]
    public async Task Read_NumberToken_ReturnsDecimal()
    {
        await Assert.That(Deserialize("""{"Value":13.4}""")).IsEqualTo(13.4m);
    }

    // Read — null token

    [Test]
    public async Task Read_NullToken_ReturnsNull()
    {
        await Assert.That(Deserialize("""{"Value":null}""")).IsNull();
    }

    // Read — unexpected token

    [Test]
    public async Task Read_BoolToken_Throws()
    {
        await Assert.That(() => Deserialize("""{"Value":true}"""))
            .Throws<JsonException>();
    }

    // ParseDecimalString — separator detection

    [Test]
    public async Task Parse_CommaDecimal_ReturnsCorrectValue()
    {
        await Assert.That(DecimalStringConverter.ParseDecimalString("0,5")).IsEqualTo(0.5m);
    }

    [Test]
    public async Task Parse_DotDecimal_ReturnsCorrectValue()
    {
        await Assert.That(DecimalStringConverter.ParseDecimalString("0.5")).IsEqualTo(0.5m);
    }

    [Test]
    public async Task Parse_CommaThousandsDotDecimal_ReturnsCorrectValue()
    {
        await Assert.That(DecimalStringConverter.ParseDecimalString("1,234.56")).IsEqualTo(1234.56m);
    }

    [Test]
    public async Task Parse_DotThousandsCommaDecimal_ReturnsCorrectValue()
    {
        await Assert.That(DecimalStringConverter.ParseDecimalString("1.234,56")).IsEqualTo(1234.56m);
    }

    [Test]
    public async Task Parse_LoneDot_TreatedAsDecimal()
    {
        await Assert.That(DecimalStringConverter.ParseDecimalString("1.234")).IsEqualTo(1.234m);
    }

    [Test]
    public async Task Parse_SpaceThousandsSeparator_Stripped()
    {
        await Assert.That(DecimalStringConverter.ParseDecimalString("1 234,56")).IsEqualTo(1234.56m);
    }

    [Test]
    public async Task Parse_NarrowNbspThousandsSeparator_Stripped()
    {
        await Assert.That(DecimalStringConverter.ParseDecimalString("1 234,56")).IsEqualTo(1234.56m);
    }

    [Test]
    public async Task Parse_WholeNumberNoSeparator_ReturnsCorrectValue()
    {
        await Assert.That(DecimalStringConverter.ParseDecimalString("1000")).IsEqualTo(1000m);
    }

    [Test]
    public async Task Parse_Null_ReturnsNull()
    {
        await Assert.That(DecimalStringConverter.ParseDecimalString(null)).IsNull();
    }

    [Test]
    public async Task Parse_Unparseable_ReturnsNull()
    {
        await Assert.That(DecimalStringConverter.ParseDecimalString("abc")).IsNull();
    }
}
