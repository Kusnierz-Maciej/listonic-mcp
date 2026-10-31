using System.Text.Json;
using System.Text.Json.Serialization;
using ListonicApiClient.Converters;

namespace ListonicApiClient.Tests.Converters;

public class ZeroOneBoolConverterTests
{
    private record Model(
        [property: JsonConverter(typeof(ZeroOneBoolConverter))] bool Value);

    private static bool Deserialize(string json) =>
        JsonSerializer.Deserialize<Model>(json)!.Value;

    private static string Serialize(bool value) =>
        JsonSerializer.Serialize(new Model(value));

    [Test]
    public async Task Read_Zero_ReturnsFalse()
    {
        await Assert.That(Deserialize("""{"Value":0}""")).IsFalse();
    }

    [Test]
    public async Task Read_One_ReturnsTrue()
    {
        await Assert.That(Deserialize("""{"Value":1}""")).IsTrue();
    }

    [Test]
    public async Task Read_NonZero_ReturnsTrue()
    {
        await Assert.That(Deserialize("""{"Value":2}""")).IsTrue();
    }

    [Test]
    public async Task Write_False_WritesZero()
    {
        await Assert.That(Serialize(false)).IsEqualTo("""{"Value":0}""");
    }

    [Test]
    public async Task Write_True_WritesOne()
    {
        await Assert.That(Serialize(true)).IsEqualTo("""{"Value":1}""");
    }

    [Test]
    public async Task Read_StringToken_Throws()
    {
        await Assert.That(() => Deserialize("""{"Value":"true"}"""))
            .Throws<JsonException>();
    }
}
