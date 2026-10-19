using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OnChainReader.Core;

/// <summary>Writes a BigInteger as a decimal STRING. JSON numbers lose precision above 2^53 in JavaScript clients, and without a converter
/// System.Text.Json serialises BigInteger as an object of its properties (found while testing against a live node).</summary>
public sealed class BigIntegerJsonConverter : JsonConverter<BigInteger>
{
    public override BigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String when BigInteger.TryParse(reader.GetString(), out var v) => v,
            JsonTokenType.Number => BigInteger.Parse(reader.GetDecimal().ToString("0")),
            _ => throw new JsonException("expected a decimal string"),
        };

    public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
