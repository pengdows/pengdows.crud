using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JsonValue = pengdows.crud.types.valueobjects.JsonValue;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// DRY-015: value-to-JSON was written three times (TypeCoercionHelper.ToJsonDocument and the
/// JsonDocument/JsonElement/JsonValue coercions), each accepting different inputs: UTF-8 bytes read
/// into a JsonDocument but threw for a JsonValue, and an empty byte segment or memory was serialized
/// as the struct itself. Every JSON-carrying input now reads the same into all three, through
/// ExtractJsonString, blank as the JSON null (COR-007).
/// </summary>
public class JsonInputParityTests
{
    public static IEnumerable<object[]> Inputs()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"a\":1}");
        yield return new object[] { "{\"a\":1}", "{\"a\":1}" };
        yield return new object[] { bytes, "{\"a\":1}" };
        yield return new object[] { new ArraySegment<byte>(bytes), "{\"a\":1}" };
        yield return new object[] { new ReadOnlyMemory<byte>(bytes), "{\"a\":1}" };
        yield return new object[] { "{\"a\":1}".ToCharArray(), "{\"a\":1}" };
        yield return new object[] { new MemoryStream(bytes), "{\"a\":1}" };
        yield return new object[] { JsonNode.Parse("{\"a\":1}")!, "{\"a\":1}" };
        yield return new object[] { "", "null" };
        yield return new object[] { Array.Empty<byte>(), "null" };
        yield return new object[] { new ArraySegment<byte>(Array.Empty<byte>()), "null" };
        yield return new object[] { ReadOnlyMemory<byte>.Empty, "null" };
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void EveryJsonInput_ReadsTheSame_IntoEveryJsonType(object raw, string json)
    {
        string Read(Type target)
        {
            if (raw is MemoryStream stream)
            {
                stream.Position = 0;
            }

            return TypeCoercionHelper.Coerce(raw, raw.GetType(), target) switch
            {
                JsonDocument doc => doc.RootElement.GetRawText(),
                JsonElement element => element.GetRawText(),
                JsonValue value => value.ToString(),
                var other => "unexpected " + other
            };
        }

        Assert.Equal(json, Read(typeof(JsonDocument)));
        Assert.Equal(json, Read(typeof(JsonElement)));
        Assert.Equal(json, Read(typeof(JsonValue)));
    }
}
