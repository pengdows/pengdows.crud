using System;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

// The fast path accepts only a value that is exactly a defined member of the enum, read from an integer without
// boxing. Anything else (an undefined number, a [Flags] combination, a value the underlying type cannot hold) is not
// the fast path's to decide: it returns false and the caller uses the full conversion, which keeps every rule and
// every failure mode.
public class EnumFastPathTests
{
    public enum Color { Red = 0, Green = 1, Blue = 2 }

    public enum Tiny : byte { A = 1, B = 2 }

    public enum Signed : sbyte { Negative = -1, Zero = 0 }

    public enum Wide : long { Big = 5_000_000_000L, Small = 1 }

    [Flags]
    public enum Perms : uint { None = 0, Read = 1, Write = 2, Exec = 4 }

    [Theory]
    [InlineData(0L, true, Color.Red)]
    [InlineData(1L, true, Color.Green)]
    [InlineData(2L, true, Color.Blue)]
    [InlineData(3L, false, default(Color))]
    [InlineData(-1L, false, default(Color))]
    [InlineData(4294967297L, false, default(Color))] // low 32 bits are 1 (Green), but the value is not Green
    public void IntEnum_AcceptsOnlyDefinedMembers(long value, bool ok, Color expected)
    {
        Assert.Equal(ok, EnumFastPath.TryFromInteger<Color>(value, out var result));
        if (ok)
        {
            Assert.Equal(expected, result);
        }
    }

    [Theory]
    [InlineData(1L, true)]
    [InlineData(2L, true)]
    [InlineData(0L, false)]
    [InlineData(257L, false)] // low byte is 1 (A), but a byte enum cannot hold 257
    [InlineData(-1L, false)]
    public void ByteEnum_RejectsWhatTheUnderlyingTypeCannotHold(long value, bool ok)
    {
        Assert.Equal(ok, EnumFastPath.TryFromInteger<Tiny>(value, out _));
    }

    [Theory]
    [InlineData(-1L, true)]
    [InlineData(0L, true)]
    [InlineData(255L, false)] // 0xFF is -1 as an sbyte, but 255 is not -1
    [InlineData(1L, false)]
    public void SignedEnum_ReadsNegativeMembers(long value, bool ok)
    {
        Assert.Equal(ok, EnumFastPath.TryFromInteger<Signed>(value, out var result));
        if (ok)
        {
            Assert.Equal((Signed)(sbyte)value, result);
        }
    }

    [Fact]
    public void LongEnum_AcceptsAMemberBeyondIntRange()
    {
        Assert.True(EnumFastPath.TryFromInteger<Wide>(5_000_000_000L, out var big));
        Assert.Equal(Wide.Big, big);
        Assert.True(EnumFastPath.TryFromInteger<Wide>(1L, out var small));
        Assert.Equal(Wide.Small, small);
        Assert.False(EnumFastPath.TryFromInteger<Wide>(2L, out _));
    }

    [Theory]
    [InlineData(0L, true)]
    [InlineData(1L, true)]
    [InlineData(2L, true)]
    [InlineData(4L, true)]
    [InlineData(3L, false)] // Read | Write: a valid combination, but the full conversion decides that
    [InlineData(7L, false)]
    [InlineData(8L, false)]
    public void FlagsEnum_AcceptsSingleMembersAndLeavesCombinationsToTheFullConversion(long value, bool ok)
    {
        Assert.Equal(ok, EnumFastPath.TryFromInteger<Perms>(value, out _));
    }

    [Fact]
    public void ValidateEnumValue_StillAcceptsADefinedValue_AFlagsCombination_AndRejectsAnUndefinedOne()
    {
        Assert.Equal(Color.Blue, EnumMappingCache.ValidateEnumValue(Color.Blue));
        Assert.Equal(Perms.Read | Perms.Write, EnumMappingCache.ValidateEnumValue(Perms.Read | Perms.Write));
        Assert.Throws<EnumValueException>(() => EnumMappingCache.ValidateEnumValue((Color)9));
    }
}
