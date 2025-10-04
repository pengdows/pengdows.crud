#region

using System;
using System.Collections.Generic;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EntityHelperEnumParseModeTests : SqlLiteContextTestBase
{
    [Table("EnumModes")]
    private class EnumModeEntity
    {
        [Id(false)] [Column("Id", DbType.Int32)] public int Id { get; set; }
        [EnumColumn(typeof(Color))] [Column("ColorText", DbType.String)] public Color? ColorText { get; set; }
        [EnumColumn(typeof(Color))] [Column("ColorNum", DbType.Int32)] public Color ColorNum { get; set; }
    }
    private enum Color { Red, Green, Blue }

    public EntityHelperEnumParseModeTests()
    {
        TypeMap.Register<EnumModeEntity>();
    }

    [Fact]
    public void SetNullAndLog_InvalidString_SetsNull()
    {
        var helper = new EntityHelper<EnumModeEntity, int>(Context, enumParseBehavior: EnumParseFailureMode.SetNullAndLog);
        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["ColorText"] = "NotAColor",
                ["ColorNum"] = 5 // invalid numeric but will be handled below
            }
        };
        using var reader = new EntityHelperConverterTests.FakeTrackedReader(rows);
        reader.Read();
        var e = helper.MapReaderToObject(reader);
        Assert.Null(e.ColorText);
    }

    [Fact]
    public void SetDefaultValue_InvalidNumeric_SetsDefault()
    {
        var helper = new EntityHelper<EnumModeEntity, int>(Context, enumParseBehavior: EnumParseFailureMode.SetDefaultValue);
        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["ColorNum"] = 12345
            }
        };
        using var reader = new EntityHelperConverterTests.FakeTrackedReader(rows);
        reader.Read();
        var e = helper.MapReaderToObject(reader);
        Assert.Equal(default(Color), e.ColorNum);
    }

    [Fact]
    public void ReplaceDialectTokens_ReplacesMarkers()
    {
        var helper = new EntityHelper<EnumModeEntity, int>(Context);
        var sql = "@p SELECT \"Name\" FROM \"T\" WHERE \"Id\"=@id";
        var replaced = helper.ReplaceDialectTokens(sql, "[", "]", ":");
        Assert.Contains(":p", replaced);
        Assert.Contains("[Name]", replaced);
        Assert.Contains("[T]", replaced);
        Assert.Contains(":id", replaced);
    }

    [Fact]
    public void Throw_InvalidNumericEnum_Throws()
    {
        var helper = new EntityHelper<EnumModeEntity, int>(Context, enumParseBehavior: EnumParseFailureMode.Throw);
        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["ColorNum"] = 9999
            }
        };
        using var reader = new EntityHelperConverterTests.FakeTrackedReader(rows);
        reader.Read();
        Assert.Throws<ArgumentException>(() => helper.MapReaderToObject(reader));
    }
}
