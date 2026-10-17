using System;
using System.Collections.Generic;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// Every real ADO.NET provider returns DBNull.Value from GetValue for a NULL cell, never a CLR null, so a caller can
// treat "is DBNull" as the null check with a single read. fakeDb stored the row's null and handed it back as null,
// which a provider never does.
public class FakeDbDataReaderNullValueTests
{
    private static fakeDbDataReader ReaderWithNullCell()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.Open();
        conn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = null }
        });

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"Id\", \"Name\" FROM \"Customers\"";
        var reader = (fakeDbDataReader)cmd.ExecuteReader();
        Assert.True(reader.Read());
        return reader;
    }

    [Fact]
    public void GetValue_OfANullCell_IsDBNull_NotNull()
    {
        using var reader = ReaderWithNullCell();

        Assert.Same(DBNull.Value, reader.GetValue(1));
    }

    [Fact]
    public void IsDBNull_AndGetValue_AgreeForANullCell()
    {
        using var reader = ReaderWithNullCell();

        Assert.True(reader.IsDBNull(1));
        Assert.IsType<DBNull>(reader.GetValue(1));
    }

    [Fact]
    public void GetValue_OfAPresentCell_IsUnchanged()
    {
        using var reader = ReaderWithNullCell();

        Assert.Equal(1, reader.GetValue(0));
    }
}
