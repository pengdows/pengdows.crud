using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;
using pengdows.crud.fakeDb;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-065: DataReaderMapper's setter for a column the provider reports no type for (TYPE-016) wrote
/// null through PropertyInfo.SetValue on a NULL, which stores a struct property's default and
/// overwrites its initializer. Every other column path leaves the property alone on NULL.
/// </summary>
public sealed class DataReaderMapperUnresolvedNullTests
{
    public sealed class Row
    {
        public int Count { get; set; } = 7;
        public string Name { get; set; } = "kept";
    }

    private static Action<Row, DbDataReader> Setter(string property) =>
        (Action<Row, DbDataReader>)typeof(DataReaderMapper)
            .GetMethod("CreateUnresolvedSetter", BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(Row))
            .Invoke(null, new object?[]
            {
                typeof(Row).GetProperty(property)!, typeof(object), EnumParseFailureMode.Throw, 0, null,
                (Func<IDataRecord, int, Type, object>)((r, i, _) => r.GetValue(i))
            })!;

    private static fakeDbDataReader NullRow()
    {
        var reader = new fakeDbDataReader(new[] { new Dictionary<string, object> { ["v"] = DBNull.Value } });
        reader.Read();
        return reader;
    }

    [Fact]
    public void NullColumn_LeavesAStructPropertyAlone()
    {
        var row = new Row();

        Setter(nameof(Row.Count))(row, NullRow());

        Assert.Equal(7, row.Count);
    }

    [Fact]
    public void NullColumn_LeavesAReferencePropertyAlone()
    {
        var row = new Row();

        Setter(nameof(Row.Name))(row, NullRow());

        Assert.Equal("kept", row.Name);
    }
}
