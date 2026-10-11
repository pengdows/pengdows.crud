using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A FrozenSet of five or more types switches from a linear scan of its items to a hash lookup, which costs about 3 ns
/// more per parameter (measured against 2.0.5: a string parameter's validation went from 38.5 to 47 ns when TextReader
/// took the String set from four types to five). The exact-type sets stay at four or fewer; a Stream or TextReader
/// subclass is accepted through the abstract-type lists instead.
/// </summary>
public class DbTypeValidatorSetSizeTests
{
    [Fact]
    public void EveryAcceptableTypeSet_HoldsNoMoreThanFourTypes()
    {
        var field = typeof(DbTypeValidator).GetField("AcceptableTypes", BindingFlags.NonPublic | BindingFlags.Static)!;
        var sets = (FrozenDictionary<DbType, FrozenSet<Type>>)field.GetValue(null)!;

        foreach (var (dbType, set) in sets)
        {
            Assert.True(set.Count <= 4, $"DbType.{dbType} accepts {set.Count} exact types; FrozenSet stops scanning linearly above four.");
        }
    }

    [Theory]
    [InlineData(DbType.String)]
    [InlineData(DbType.AnsiString)]
    public void ATextReaderSubclass_IsAcceptedAsText(DbType dbType) =>
        DbTypeValidator.Validate(dbType, typeof(StringReader));

    [Fact]
    public void TextReaderItself_IsAcceptedAsText() => DbTypeValidator.Validate(DbType.String, typeof(TextReader));

    [Fact]
    public void AStreamSubclass_IsAcceptedAsBinary() => DbTypeValidator.Validate(DbType.Binary, typeof(MemoryStream));

    [Fact]
    public void StreamItself_IsAcceptedAsBinary() => DbTypeValidator.Validate(DbType.Binary, typeof(Stream));

    [Fact]
    public void AGuid_IsAcceptedAsBinary() => DbTypeValidator.Validate(DbType.Binary, typeof(Guid));

    [Fact]
    public void ATextReader_IsNotAcceptedAsAnInteger() =>
        Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(DbType.Int32, typeof(StringReader)));

    [Fact]
    public void AStream_IsNotAcceptedAsText() =>
        Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(DbType.String, typeof(MemoryStream)));

    [Fact]
    public void ATextReader_IsNotAcceptedAsBinary() =>
        Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(DbType.Binary, typeof(StringReader)));

    // The object overload follows the same rules as the Type overload and IsCompatible, so a value is accepted or rejected
    // the same whichever way it reaches the validator.
    [Fact]
    public void AGuidValue_IsAcceptedAsBinary() => DbTypeValidator.Validate(DbType.Binary, (object)Guid.NewGuid());

    [Fact]
    public void AStreamValue_IsAcceptedAsBinary()
    {
        using var stream = new MemoryStream();
        DbTypeValidator.Validate(DbType.Binary, (object)stream);
    }

    [Theory]
    [InlineData(DbType.String)]
    [InlineData(DbType.AnsiString)]
    public void ATextReaderValue_IsAcceptedAsText(DbType dbType)
    {
        using var reader = new StringReader("x");
        DbTypeValidator.Validate(dbType, (object)reader);
    }

    [Theory]
    [InlineData(DbType.StringFixedLength)]
    [InlineData(DbType.AnsiStringFixedLength)]
    public void ATextReader_IsNotAcceptedAsFixedLengthText(DbType dbType)
    {
        Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(dbType, typeof(StringReader)));
        using var reader = new StringReader("x");
        Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(dbType, (object)reader));
    }

    [Theory]
    [InlineData(DbType.Int32)]
    [InlineData(DbType.String)]
    [InlineData(DbType.Object)]
    public void AnEnum_IsAcceptedForNumericStringAndObject(DbType dbType)
    {
        DbTypeValidator.Validate(dbType, typeof(DayOfWeek));
        DbTypeValidator.Validate(dbType, (object)DayOfWeek.Monday);
    }

    [Fact]
    public void AnEnum_IsRejectedForBoolean()
    {
        var ex = Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(DbType.Boolean, typeof(DayOfWeek)));
        Assert.Contains("enum", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ANullableValue_IsValidatedAsItsUnderlyingType()
    {
        int? value = 5;
        DbTypeValidator.Validate(DbType.Int32, (object)value);
        Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(DbType.Boolean, (object)value));
    }

    [Fact]
    public void AnUnknownDbType_RejectsANumericTypeAndPassesAnythingElse()
    {
        var unknown = (DbType)9999;
        Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(unknown, typeof(int)));
        Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(unknown, (object)5));
        DbTypeValidator.Validate(unknown, typeof(DateTime));
        DbTypeValidator.Validate(unknown, (object)DateTime.UtcNow);
    }

    [Fact]
    public void IsCompatible_AgreesWithValidate()
    {
        using var stream = new MemoryStream();
        Assert.True(DbTypeValidator.IsCompatible(DbType.Binary, stream));
        Assert.True(DbTypeValidator.IsCompatible(DbType.Binary, Guid.NewGuid()));
        Assert.False(DbTypeValidator.IsCompatible(DbType.Int32, stream));
    }
}
