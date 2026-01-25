using System;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public class StringBuilderLiteTests
{
    [Fact]
    public void Append_Char_String_Span_BuildsExpected()
    {
        var sb = SbLite.Create(stackalloc char[8]);
        sb.Append('A');
        sb.Append("BC");
        sb.Append("DEF".AsSpan());
        Assert.Equal("ABCDEF", sb.ToString());
        Assert.Equal(6, sb.Length);
    }

    [Fact]
    public void AppendLine_AppendsNewlines()
    {
        var sb = SbLite.Create(stackalloc char[8]);
        sb.AppendLine("A");
        sb.AppendLine();
        Assert.Equal("A\n\n", sb.ToString());
    }

    [Fact]
    public void AppendSpan_AllowsDirectFill()
    {
        var sb = SbLite.Create(stackalloc char[8]);
        var span = sb.AppendSpan(3);
        span[0] = 'X';
        span[1] = 'Y';
        span[2] = 'Z';
        Assert.Equal("XYZ", sb.ToString());
    }

    [Fact]
    public void Append_Int_And_Long_UsesInvariant()
    {
        var sb = SbLite.Create(stackalloc char[32]);
        sb.Append(123);
        sb.Append(' ');
        sb.Append(4567890123L);
        Assert.Equal("123 4567890123", sb.ToString());
    }

    [Fact]
    public void Clear_ResetsLength()
    {
        var sb = SbLite.Create(stackalloc char[8]);
        sb.Append("abc");
        sb.Clear();
        sb.Append('d');
        Assert.Equal("d", sb.ToString());
        Assert.Equal(1, sb.Length);
    }

    [Fact]
    public void Grow_ExpandsBeyondInitialSpan()
    {
        var sb = SbLite.Create(stackalloc char[4]);
        sb.Append("abcdefgh");
        Assert.Equal("abcdefgh", sb.ToString());
        Assert.Equal(8, sb.Length);
    }

    [Fact]
    public void Append_NullOrEmpty_NoChange()
    {
        var sb = SbLite.Create(stackalloc char[8]);
        sb.Append((string?)null);
        sb.Append(string.Empty);
        sb.Append('A');
        Assert.Equal("A", sb.ToString());
    }
}