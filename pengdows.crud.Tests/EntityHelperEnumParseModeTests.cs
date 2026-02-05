#region

using System;
using System.Collections.Generic;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TableGatewayEnumParseModeTests : SqlLiteContextTestBase
{
    [Table("EnumModes")]
    private class EnumModeEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [EnumColumn(typeof(Color))]
        [Column("ColorText", DbType.String)]
        public Color? ColorText { get; set; }

        [EnumColumn(typeof(Color))]
        [Column("ColorNum", DbType.Int32)]
        public Color ColorNum { get; set; }
    }

    private enum Color
    {
        Red,
        Green,
        Blue
    }

    public TableGatewayEnumParseModeTests()
    {
        TypeMap.Register<EnumModeEntity>();
    }

    [Fact]
    public void SetNullAndLog_InvalidString_SetsNull()
    {
        var helper =
            new TableGateway<EnumModeEntity, int>(Context, enumParseBehavior: EnumParseFailureMode.SetNullAndLog);
        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["ColorText"] = "NotAColor",
                ["ColorNum"] = 5 // invalid numeric but will be handled below
            }
        };
        using var reader = new TableGatewayConverterTests.FakeTrackedReader(rows);
        reader.Read();
        var e = helper.MapReaderToObject(reader);
        Assert.Null(e.ColorText);
    }

    [Fact]
    public void SetDefaultValue_InvalidNumeric_SetsDefault()
    {
        var helper =
            new TableGateway<EnumModeEntity, int>(Context, enumParseBehavior: EnumParseFailureMode.SetDefaultValue);
        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["ColorNum"] = 12345
            }
        };
        using var reader = new TableGatewayConverterTests.FakeTrackedReader(rows);
        reader.Read();
        var e = helper.MapReaderToObject(reader);
        Assert.Equal(default, e.ColorNum);
    }

    [Fact]
    public void ReplaceNeutralTokens_ReplacesMarkers()
    {
        var helper = new TableGateway<EnumModeEntity, int>(Context);
        var dialect = ((ISqlDialectProvider)Context).Dialect;
        var replaced = helper.ReplaceNeutralTokens("{Q}Name{q} FROM {Q}T{q} WHERE {Q}Id{q}={S}id");
        var expected = $"{dialect.WrapObjectName("Name")} FROM {dialect.WrapObjectName("T")} WHERE {dialect.WrapObjectName("Id")}={dialect.ParameterMarker}id";
        Assert.Equal(expected, replaced);
    }

    [Fact]
    public void Throw_InvalidNumericEnum_Throws()
    {
        var helper = new TableGateway<EnumModeEntity, int>(Context, enumParseBehavior: EnumParseFailureMode.Throw);
        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["ColorNum"] = 9999
            }
        };
        using var reader = new TableGatewayConverterTests.FakeTrackedReader(rows);
        reader.Read();
        Assert.Throws<ArgumentException>(() => helper.MapReaderToObject(reader));
    }
}
