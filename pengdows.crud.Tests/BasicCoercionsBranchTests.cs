using System;
using System.Globalization;
using System.Text.Json;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests;

public class BasicCoercionsBranchTests
{
    [Fact]
    public void GuidCoercion_ReadsMultipleFormats()
    {
        var guid = Guid.NewGuid();
        var bytes = guid.ToByteArray();
        var coercion = new GuidCoercion();

        Assert.True(coercion.TryRead(new DbValue(bytes), out var fromBytes));
        Assert.Equal(guid, fromBytes);

        var memory = new ReadOnlyMemory<byte>(bytes);
        Assert.True(coercion.TryRead(new DbValue(memory), out var fromMemory));
        Assert.Equal(guid, fromMemory);

        var segment = new ArraySegment<byte>(bytes);
        Assert.True(coercion.TryRead(new DbValue(segment), out var fromSegment));
        Assert.Equal(guid, fromSegment);

        var chars = guid.ToString("D", CultureInfo.InvariantCulture).ToCharArray();
        Assert.True(coercion.TryRead(new DbValue(chars), out var fromChars));
        Assert.Equal(guid, fromChars);
    }

    [Fact]
    public void BooleanCoercion_ReadsStringsCharsAndNumbers()
    {
        var coercion = new BooleanCoercion();

        Assert.True(coercion.TryRead(new DbValue("t"), out var fromString));
        Assert.True(fromString);

        Assert.True(coercion.TryRead(new DbValue("0"), out var fromZero));
        Assert.False(fromZero);

        Assert.True(coercion.TryRead(new DbValue('y'), out var fromChar));
        Assert.True(fromChar);

        Assert.True(coercion.TryRead(new DbValue(2L), out var fromNumber));
        Assert.True(fromNumber);

        Assert.True(coercion.TryRead(new DbValue(0.0f), out var fromFloat));
        Assert.False(fromFloat);

        Assert.True(coercion.TryRead(new DbValue(1.5m), out var fromDecimal));
        Assert.True(fromDecimal);
    }

    [Fact]
    public void BooleanCoercion_InvalidChar_Throws()
    {
        var coercion = new BooleanCoercion();

        Assert.Throws<InvalidCastException>(() =>
            coercion.TryRead(new DbValue("x"), out _));
    }

    [Fact]
    public void DateTimeCoercion_HandlesKindsAndStrings()
    {
        var coercion = new DateTimeCoercion();
        var local = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Local);

        Assert.True(coercion.TryRead(new DbValue(local), out var fromLocal));
        Assert.Equal(DateTimeKind.Utc, fromLocal.Kind);

        var dtoText = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero).ToString("O", CultureInfo.InvariantCulture);
        Assert.True(coercion.TryRead(new DbValue(dtoText), out var fromDtoString));
        Assert.Equal(DateTimeKind.Utc, fromDtoString.Kind);

        var dtText = new DateTime(2024, 1, 3, 4, 5, 6, DateTimeKind.Unspecified).ToString("O", CultureInfo.InvariantCulture);
        Assert.True(coercion.TryRead(new DbValue(dtText), out var fromDtString));
        Assert.Equal(DateTimeKind.Utc, fromDtString.Kind);
    }

    [Fact]
    public void DateTimeOffsetAndTimeSpanCoercions_HandleFallbacks()
    {
        var dtoCoercion = new DateTimeOffsetCoercion();
        var tsCoercion = new TimeSpanCoercion();

        Assert.True(dtoCoercion.TryRead(new DbValue(DateTime.UtcNow), out var dto));
        Assert.NotEqual(DateTimeOffset.MinValue, dto);

        Assert.True(tsCoercion.TryRead(new DbValue(1.5), out var ts));
        Assert.Equal(TimeSpan.FromSeconds(1.5), ts);

        Assert.True(tsCoercion.TryRead(new DbValue("00:01:00"), out var fromString));
        Assert.Equal(TimeSpan.FromMinutes(1), fromString);
    }

    [Fact]
    public void DecimalAndByteArrayCoercions_HandleConversions()
    {
        var decimalCoercion = new DecimalCoercion();
        Assert.True(decimalCoercion.TryRead(new DbValue("12.5", typeof(string)), out var dec));
        Assert.Equal(12.5m, dec);

        Assert.False(decimalCoercion.TryRead(new DbValue("nope", typeof(string)), out _));

        var bytes = new byte[] { 1, 2, 3 };
        var byteCoercion = new ByteArrayCoercion();
        Assert.True(byteCoercion.TryRead(new DbValue(new ReadOnlyMemory<byte>(bytes)), out var fromMemory));
        Assert.Equal(bytes, fromMemory);

        var segment = new ArraySegment<byte>(bytes, 1, 2);
        Assert.True(byteCoercion.TryRead(new DbValue(segment), out var fromSegment));
        Assert.Equal(new byte[] { 2, 3 }, fromSegment);
    }

    [Fact]
    public void JsonDocumentAndElementCoercions_HandleInputs()
    {
        var docCoercion = new JsonDocumentCoercion();
        var elementCoercion = new JsonElementCoercion();

        using var doc = JsonDocument.Parse("{\"a\":1}");
        Assert.True(docCoercion.TryRead(new DbValue(doc.RootElement), out var fromElement));
        Assert.NotNull(fromElement);

        Assert.True(docCoercion.TryRead(new DbValue("{\"b\":2}"), out var fromString));
        Assert.NotNull(fromString);

        Assert.False(docCoercion.TryRead(new DbValue(""), out _));

        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"c\":3}");
        Assert.True(elementCoercion.TryRead(new DbValue(bytes), out var element));
        Assert.Equal("3", element.GetProperty("c").ToString());

        Assert.True(elementCoercion.TryRead(new DbValue(doc), out var fromDoc));
        Assert.Equal("1", fromDoc.GetProperty("a").ToString());
    }
}
