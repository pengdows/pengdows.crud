using System;
using System.Collections.Generic;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// COR-005 (found by the 2026-10-04 performance review): a string enum column went through
/// Enum.Parse, which accepts a number ("999") and returns an undefined enum value, while the numeric
/// path validated. Both paths now accept exactly the defined values, plus, for a [Flags] enum, any
/// combination of defined flags (which the numeric path wrongly rejected).
/// </summary>
public class EnumValidationTests : SqlLiteContextTestBase
{
    private enum Color { Red, Green, Blue }

    [Flags]
    private enum Access { None = 0, Read = 1, Write = 2, Execute = 4 }

    [Table("Enums")]
    private class Row
    {
        [Id(false)] [Column("Id", DbType.Int32)] public int Id { get; set; }
        [EnumColumn(typeof(Color))] [Column("ColorText", DbType.String)] public Color ColorText { get; set; }
        [EnumColumn(typeof(Access))] [Column("AccessText", DbType.String)] public Access AccessText { get; set; }
        [EnumColumn(typeof(Access))] [Column("AccessNum", DbType.Int32)] public Access AccessNum { get; set; }
    }

    private Row Map(object colorText, object accessText, object accessNum)
    {
        var helper = new TableGateway<Row, int>(Context);
        using var reader = new TableGatewayConverterTests.FakeTrackedReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1, ["ColorText"] = colorText, ["AccessText"] = accessText, ["AccessNum"] = accessNum
            }
        });
        reader.Read();
        return helper.MapReaderToObject(reader);
    }

    [Fact]
    public void StringEnum_UndefinedNumber_Throws()
    {
        Assert.Throws<ArgumentException>(() => Map("999", "Read", 1));
    }

    [Fact]
    public void StringEnum_DefinedNumber_IsRead()
    {
        Assert.Equal(Color.Blue, Map("2", "Read", 1).ColorText);
    }

    [Fact]
    public void FlagsEnum_CombinationOfDefinedFlags_IsRead()
    {
        var row = Map("Red", "Read, Write", 3);

        Assert.Equal(Access.Read | Access.Write, row.AccessText);
        Assert.Equal(Access.Read | Access.Write, row.AccessNum);
    }

    [Fact]
    public void FlagsEnum_UndefinedBit_Throws()
    {
        Assert.Throws<ArgumentException>(() => Map("Red", "Read", 8));
        Assert.Throws<ArgumentException>(() => Map("Red", "9", 1));
    }
}
