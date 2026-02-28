using System;
using System.Data.Common;
using System.Text.Json;
using Moq;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests;

public class BasicCoercionsBranchTests2
{
    [Fact]
    public void GuidCoercion_HandlesInvalidInputs()
    {
        var coercion = new GuidCoercion();
        Assert.False(coercion.TryRead(new DbValue(null), out _));
        Assert.False(coercion.TryRead(new DbValue("not-a-guid", typeof(string)), out _));
        Assert.False(coercion.TryRead(new DbValue(new byte[] { 1, 2, 3 }, typeof(byte[])), out _));

        var guid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        Assert.True(coercion.TryRead(new DbValue(guid.ToByteArray()), out var fromBytes));
        Assert.Equal(guid, fromBytes);
    }

    [Fact]
    public void DateTimeOffsetCoercion_HandlesNullAndDateTime()
    {
        var coercion = new DateTimeOffsetCoercion();
        Assert.False(coercion.TryRead(new DbValue(null), out _));

        var dt = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(coercion.TryRead(new DbValue(dt), out var result));
        Assert.Equal(dt, result.UtcDateTime);
    }

    [Fact]
    public void TimeSpanCoercion_HandlesInvalidString()
    {
        var coercion = new TimeSpanCoercion();
        Assert.False(coercion.TryRead(new DbValue("not-a-timespan", typeof(string)), out _));
        Assert.True(coercion.TryRead(new DbValue(1.5d), out var result));
        Assert.Equal(TimeSpan.FromSeconds(1.5), result);
    }

    [Fact]
    public void ArrayCoercions_HandleNullsAndInvalid()
    {
        var intCoercion = new IntArrayCoercion();
        Assert.False(intCoercion.TryRead(new DbValue(null), out _));
        Assert.False(intCoercion.TryRead(new DbValue("bad", typeof(string)), out _));

        var param = new Mock<DbParameter>();
        param.SetupAllProperties();
        Assert.True(intCoercion.TryWrite(null, param.Object));
        Assert.Equal(DBNull.Value, param.Object.Value);

        var stringCoercion = new StringArrayCoercion();
        Assert.False(stringCoercion.TryRead(new DbValue(123), out _));
        Assert.True(stringCoercion.TryWrite(null, param.Object));
        Assert.Equal(DBNull.Value, param.Object.Value);
    }

    [Fact]
    public void JsonValueCoercion_HandlesInvalidString()
    {
        var coercion = new JsonValueCoercion();
        Assert.False(coercion.TryRead(new DbValue(null), out _));
        Assert.False(coercion.TryRead(new DbValue("not-json", typeof(string)), out _));
        Assert.True(coercion.TryRead(new DbValue(JsonDocument.Parse("{}")), out var value));
        Assert.Equal("{}", value.AsString());
    }

    [Fact]
    public void HStoreCoercion_HandlesInvalidString()
    {
        var coercion = new HStoreCoercion();
        Assert.False(coercion.TryRead(new DbValue(null), out _));
        Assert.False(coercion.TryRead(new DbValue("badpair", typeof(string)), out _));
    }

    [Fact]
    public void RangeCoercions_RejectInvalidStrings()
    {
        var intCoercion = new IntRangeCoercion();
        var dateCoercion = new DateTimeRangeCoercion();

        Assert.False(intCoercion.TryRead(new DbValue(null), out _));
        Assert.False(intCoercion.TryRead(new DbValue("bad", typeof(string)), out _));
        Assert.False(dateCoercion.TryRead(new DbValue("bad", typeof(string)), out _));
    }

    [Fact]
    public void BooleanCoercion_HandlesInvalidInput()
    {
        var coercion = new BooleanCoercion();
        Assert.False(coercion.TryRead(new DbValue("nope", typeof(string)), out _));

        Assert.Throws<InvalidCastException>(() =>
            coercion.TryRead(new DbValue('x', typeof(char)), out _));
    }

    [Fact]
    public void DateTimeCoercion_HandlesInvalidString()
    {
        var coercion = new DateTimeCoercion();
        Assert.False(coercion.TryRead(new DbValue("bad", typeof(string)), out _));

        var dto = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.True(coercion.TryRead(new DbValue(dto), out var fromDto));
        Assert.Equal(DateTimeKind.Utc, fromDto.Kind);
    }

    [Fact]
    public void DecimalCoercion_HandlesInvalidInput()
    {
        var coercion = new DecimalCoercion();
        Assert.False(coercion.TryRead(new DbValue("bad", typeof(string)), out _));
    }

    [Fact]
    public void ByteArrayCoercion_HandlesMemoryAndInvalid()
    {
        var coercion = new ByteArrayCoercion();
        var memory = new ReadOnlyMemory<byte>(new byte[] { 1, 2 });
        var segment = new ArraySegment<byte>(new byte[] { 3, 4 });

        Assert.True(coercion.TryRead(new DbValue(memory), out var fromMemory));
        Assert.Equal(new byte[] { 1, 2 }, fromMemory);
        Assert.True(coercion.TryRead(new DbValue(segment), out var fromSegment));
        Assert.Equal(new byte[] { 3, 4 }, fromSegment);
        Assert.False(coercion.TryRead(new DbValue(123), out _));
    }

    [Fact]
    public void JsonDocumentCoercion_HandlesEmptyAndInvalid()
    {
        var coercion = new JsonDocumentCoercion();
        Assert.False(coercion.TryRead(new DbValue("", typeof(string)), out _));
        Assert.False(coercion.TryRead(new DbValue("bad", typeof(string)), out _));

        using var doc = JsonDocument.Parse("{\"a\":1}");
        Assert.True(coercion.TryRead(new DbValue(doc.RootElement.Clone()), out var fromElement));
        Assert.Equal("1", fromElement!.RootElement.GetProperty("a").GetRawText());
    }

    [Fact]
    public void JsonElementCoercion_HandlesInvalidString()
    {
        var coercion = new JsonElementCoercion();
        Assert.False(coercion.TryRead(new DbValue(null), out _));
        Assert.False(coercion.TryRead(new DbValue("bad", typeof(string)), out _));
    }
}