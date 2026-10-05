using System;
using System.Globalization;
using System.IO;
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

    // Snowflake.Data (and some other drivers) return a TIME column as a DateTime anchored to a date,
    // so a TimeSpan property must take its time of day. TYPE-001.
    [Fact]
    public void TimeSpanCoercion_ReadsTimeOfDayFromDateTimeAndTimeOnly()
    {
        var tsCoercion = new TimeSpanCoercion();

        Assert.True(tsCoercion.TryRead(new DbValue(new DateTime(1970, 1, 1, 13, 45, 30)), out var fromDateTime));
        Assert.Equal(new TimeSpan(13, 45, 30), fromDateTime);

        Assert.True(tsCoercion.TryRead(new DbValue(new TimeOnly(13, 45, 30)), out var fromTimeOnly));
        Assert.Equal(new TimeSpan(13, 45, 30), fromTimeOnly);
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

    [Fact]
    public void HStoreCoercion_ReadsNpgsqlDictionaryShape()
    {
        // Npgsql 9 hydrates a real hstore column as Dictionary<string, string?>, not text.
        var raw = new System.Collections.Generic.Dictionary<string, string?>
        {
            ["role"] = "admin",
            ["nickname"] = null
        };

        Assert.True(new HStoreCoercion().TryRead(new DbValue(raw), out var value));
        Assert.Equal("admin", value["role"]);
        Assert.True(value.ContainsKey("nickname"));
        Assert.Null(value["nickname"]);
        Assert.Equal(2, value.Count);
    }

    [Fact]
    public void ByteArrayCoercion_StreamPath_HandlesSeekAndCopyFailure()
    {
        var coercion = new ByteArrayCoercion();
        using var stream = new MemoryStream(new byte[] { 5, 6, 7, 8 });
        stream.Position = 2;

        Assert.True(coercion.TryRead(new DbValue(stream), out var bytes));
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, bytes);

        var throwing = new ThrowingReadStream();
        Assert.False(coercion.TryRead(new DbValue(throwing), out _));
    }

    private sealed class ThrowingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => 1;
        public override long Position { get; set; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("read-fail");
        public override long Seek(long offset, SeekOrigin origin) => Position = 0;
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
