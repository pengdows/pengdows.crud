using System;
using System.Collections.Generic;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Real providers report a column's type from its declared type, whatever the current row holds, and
/// never report NULL as <see cref="DBNull"/>. fakeDb used GetValue(ordinal).GetType(), which threw
/// NullReferenceException for a null value and reported DBNull for DBNull.Value, so a result set
/// whose first row held NULL could not be mapped at all.
/// </summary>
public sealed class FakeDbDataReaderGetFieldTypeTests
{
    [Fact]
    public void GetFieldType_NullInFirstRow_ReportsTheColumnsNonNullValueType()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["a"] = null!, ["b"] = DBNull.Value },
            new Dictionary<string, object> { ["a"] = 5, ["b"] = "text" }
        });

        Assert.Equal(typeof(int), reader.GetFieldType(0));
        Assert.Equal(typeof(string), reader.GetFieldType(1));
        Assert.True(reader.Read());
        Assert.Equal(typeof(int), reader.GetFieldType(0));
        Assert.Equal(typeof(string), reader.GetFieldType(1));
    }

    [Fact]
    public void GetFieldType_AllNull_ReportsObject()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["a"] = null!, ["b"] = DBNull.Value }
        });

        Assert.Equal(typeof(object), reader.GetFieldType(0));
        Assert.Equal(typeof(object), reader.GetFieldType(1));
    }

    [Fact]
    public void GetFieldType_NonNullValue_ReportsItsType()
    {
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["a"] = 1L }
        });

        Assert.True(reader.Read());
        Assert.Equal(typeof(long), reader.GetFieldType(0));
    }
}
