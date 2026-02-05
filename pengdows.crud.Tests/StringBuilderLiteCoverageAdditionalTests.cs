// Exercises branches in StringBuilderLite that the base StringBuilderLiteTests
// do not reach: negative-length AppendSpan throw, zero-initial-buffer Grow,
// char-at-exact-boundary Grow, negative int/long formatting.

using System;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class StringBuilderLiteCoverageAdditionalTests
{
    [Fact]
    public void AppendSpan_NegativeLength_Throws()
    {
        var sb = SbLite.Create(stackalloc char[8]);
        try
        {
            sb.AppendSpan(-1);
            Assert.Fail("Expected ArgumentOutOfRangeException");
        }
        catch (ArgumentOutOfRangeException) { /* expected */ }
    }

    [Fact]
    public void AppendSpan_ZeroLength_NoOp()
    {
        var sb = SbLite.Create(stackalloc char[8]);
        sb.Append("AB");
        var span = sb.AppendSpan(0);
        Assert.Equal(0, span.Length);
        Assert.Equal("AB", sb.ToString());
    }

    [Fact]
    public void Append_CharAtExactBoundary_GrowsThenAppends()
    {
        // Buffer of 3; fill exactly, then one more char triggers Grow
        var sb = SbLite.Create(stackalloc char[3]);
        sb.Append('A');
        sb.Append('B');
        sb.Append('C');   // fills buffer
        sb.Append('D');   // must grow

        Assert.Equal("ABCD", sb.ToString());
        Assert.Equal(4, sb.Length);
    }

    [Fact]
    public void Append_NegativeInt_FormatsCorrectly()
    {
        var sb = SbLite.Create(stackalloc char[16]);
        sb.Append(-42);
        Assert.Equal("-42", sb.ToString());
    }

    [Fact]
    public void Append_IntMinValue_FormatsCorrectly()
    {
        var sb = SbLite.Create(stackalloc char[16]);
        sb.Append(int.MinValue);
        Assert.Equal("-2147483648", sb.ToString());
    }

    [Fact]
    public void Append_NegativeLong_FormatsCorrectly()
    {
        var sb = SbLite.Create(stackalloc char[32]);
        sb.Append(-9876543210L);
        Assert.Equal("-9876543210", sb.ToString());
    }

    [Fact]
    public void Append_LongMinValue_FormatsCorrectly()
    {
        var sb = SbLite.Create(stackalloc char[32]);
        sb.Append(long.MinValue);
        Assert.Equal("-9223372036854775808", sb.ToString());
    }

    [Fact]
    public void ZeroInitialBuffer_GrowsImmediately()
    {
        // Span<char> of length 0 forces Grow on the very first Append
        var sb = SbLite.Create(stackalloc char[0]);
        sb.Append("hello");
        Assert.Equal("hello", sb.ToString());
    }

    [Fact]
    public void AppendSpan_TriggerGrow_ThenWritable()
    {
        // Buffer 4, already has 3 chars; AppendSpan(3) needs 6 total → Grow
        var sb = SbLite.Create(stackalloc char[4]);
        sb.Append("ABC");
        var span = sb.AppendSpan(3);
        span[0] = '1';
        span[1] = '2';
        span[2] = '3';
        Assert.Equal("ABC123", sb.ToString());
    }

    [Fact]
    public void MultipleGrows_AccumulateCorrectly()
    {
        // Start tiny, grow multiple times
        var sb = SbLite.Create(stackalloc char[2]);
        sb.Append("AA");       // fills 2
        sb.Append("BB");       // grow to 4, fills 4
        sb.Append("CCCC");     // grow to 8, fills 8
        sb.Append("DDDDDDDD"); // grow to 16, fills 16
        Assert.Equal("AABBCCCCDDDDDDDD", sb.ToString());
        Assert.Equal(16, sb.Length);
    }

    [Fact]
    public void Clear_ThenAppendIntAndLong_ReusesBuffer()
    {
        var sb = SbLite.Create(stackalloc char[32]);
        sb.Append("discard");
        sb.Clear();
        sb.Append(7);
        sb.Append(' ');
        sb.Append(99L);
        Assert.Equal("7 99", sb.ToString());
    }
}
