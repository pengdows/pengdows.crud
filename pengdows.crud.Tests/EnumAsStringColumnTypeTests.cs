#region

using System.Data;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// An enum stored in any string column type is written as its name, never its numeric value.
/// </summary>
public class EnumAsStringColumnTypeTests
{
    public enum Status
    {
        Pending = 0,
        Shipped = 7
    }

    private sealed class Holder
    {
        public Status Value { get; set; } = Status.Shipped;
    }

    [Table("enum_ansi")]
    private sealed class AnsiEnumEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("status", DbType.AnsiString)]
        [EnumColumn(typeof(Status))]
        public Status Status { get; set; } = Status.Shipped;
    }

    [Theory]
    [InlineData(DbType.String)]
    [InlineData(DbType.AnsiString)]
    [InlineData(DbType.StringFixedLength)]
    [InlineData(DbType.AnsiStringFixedLength)]
    public void MakeParameterValueFromField_StringColumnTypes_WriteEnumName(DbType dbType)
    {
        var column = new ColumnInfo
        {
            Name = "Value",
            PropertyInfo = typeof(Holder).GetProperty(nameof(Holder.Value))!,
            DbType = dbType,
            EnumType = typeof(Status),
            EnumUnderlyingType = typeof(int)
        };

        Assert.Equal("Shipped", column.MakeParameterValueFromField(new Holder()));
    }

    [Fact]
    public void RegisteredAnsiStringEnumColumn_WritesEnumName()
    {
        var registry = new TypeMapRegistry();
        var column = registry.GetTableInfo<AnsiEnumEntity>().Columns["status"];

        Assert.True(column.EnumAsString);
        Assert.Equal("Shipped", column.MakeParameterValueFromField(new AnsiEnumEntity()));
    }
}
