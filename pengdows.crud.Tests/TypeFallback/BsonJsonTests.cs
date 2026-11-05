using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// SingleStore's BSON column returns the binary document. It reads as the JSON text of that document, the same JSON a JSON
/// column returns. BSON is a little-endian length, then elements (a type byte, a name, the value), then a zero byte.
/// </summary>
public class BsonJsonTests
{
    // {"b":2}: int32 element 0x10.
    private static readonly byte[] IntDocument =
        { 0x0C, 0, 0, 0, 0x10, (byte)'b', 0, 2, 0, 0, 0, 0 };

    [Fact]
    public void AnInt32DocumentIsItsJson()
    {
        Assert.Equal("{\"b\":2}", BsonJson.ToJson(IntDocument));
    }

    [Fact]
    public void StringBoolNullAndDoubleElementsAreTheirJsonValues()
    {
        // {"s":"hi","t":true,"n":null,"d":1.5}
        var bytes = Document(
            Element(0x02, "s", Str("hi")),
            Element(0x08, "t", new byte[] { 1 }),
            Element(0x0A, "n", Array.Empty<byte>()),
            Element(0x01, "d", BitConverter.GetBytes(1.5)));

        Assert.Equal("{\"s\":\"hi\",\"t\":true,\"n\":null,\"d\":1.5}", BsonJson.ToJson(bytes));
    }

    [Fact]
    public void NestedDocumentsAndArraysAreTheirJson()
    {
        // {"a":{"x":1},"l":[10,20]}
        var inner = Document(Element(0x10, "x", BitConverter.GetBytes(1)));
        var array = Document(Element(0x10, "0", BitConverter.GetBytes(10)), Element(0x10, "1", BitConverter.GetBytes(20)));
        var bytes = Document(Element(0x03, "a", inner), Element(0x04, "l", array));

        Assert.Equal("{\"a\":{\"x\":1},\"l\":[10,20]}", BsonJson.ToJson(bytes));
    }

    [Fact]
    public void AnInt64ElementIsItsValue()
    {
        var bytes = Document(Element(0x12, "big", BitConverter.GetBytes(9007199254740993L)));

        Assert.Equal("{\"big\":9007199254740993}", BsonJson.ToJson(bytes));
    }

    [Fact]
    public void ASingleStoreBinaryJsonColumnReadsAsItsJsonDocument()
    {
        Assert.True(new JsonDocumentConverter().TryConvertFromProvider(IntDocument, SupportedDatabase.SingleStore, out var document));

        Assert.Equal(2, document!.RootElement.GetProperty("b").GetInt32());
    }

    [Fact]
    public void JsonTextBytesAreNotTakenForBson()
    {
        // The bytes of "{}" (not a BSON document: its first int32 is not its length) still read through the JSON reader.
        Assert.True(new JsonDocumentConverter().TryConvertFromProvider(Encoding.UTF8.GetBytes("{}"), SupportedDatabase.SingleStore,
            out var document));
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }

    private static byte[] Str(string value)
    {
        var text = Encoding.UTF8.GetBytes(value);
        var length = BitConverter.GetBytes(text.Length + 1);
        return [.. length, .. text, 0];
    }

    private static byte[] Element(byte type, string name, byte[] value) =>
        [type, .. Encoding.UTF8.GetBytes(name), 0, .. value];

    private static byte[] Document(params byte[][] elements)
    {
        var body = new List<byte>();
        foreach (var element in elements)
        {
            body.AddRange(element);
        }

        body.Add(0);
        var length = BitConverter.GetBytes(body.Count + 4);
        return [.. length, .. body];
    }
}

/// <summary>
/// The mapper's JSON document read (the coercion, not only the converter) reads a SingleStore BSON column the same way.
/// </summary>
public class BsonJsonCoercionPathTests
{
    [Fact]
    public void TheSingleStoreJsonDocumentCoercionReadsTheBsonBytes()
    {
        var options = pengdows.crud.TypeCoercionOptions.For(
            pengdows.crud.dialects.SqlDialectFactory.CreateDialectForType(SupportedDatabase.SingleStore,
                new pengdows.crud.fakeDb.fakeDbFactory(SupportedDatabase.SingleStore),
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
        var bson = new byte[] { 0x0C, 0, 0, 0, 0x10, (byte)'b', 0, 2, 0, 0, 0, 0 };

        var document = Assert.IsType<JsonDocument>(
            pengdows.crud.TypeCoercionHelper.Coerce(bson, typeof(byte[]), typeof(JsonDocument), options));

        Assert.Equal(2, document.RootElement.GetProperty("b").GetInt32());
    }
}
