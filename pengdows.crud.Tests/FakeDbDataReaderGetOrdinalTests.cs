using System.Collections.Generic;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// A cross-provider testbed probe (TestProvider.TestGetOrdinalUnknownColumnBehavior) found that
// every real ADO.NET provider tested — SQL Server, MySQL, PostgreSQL (and Yugabyte), MariaDB,
// CockroachDB, Firebird, Oracle, Db2, TiDB, SQLite, DuckDB, across 30 engine/version targets —
// throws some exception from GetOrdinal for an unknown column name; 25 of the 30 throw exactly
// IndexOutOfRangeException, the type documented on IDataRecord.GetOrdinal. fakeDbDataReader was
// the sole outlier, silently returning -1. These tests lock in the corrected, provider-matching
// behavior.
public class FakeDbDataReaderGetOrdinalTests
{
    private static fakeDbDataReader CreateReaderWithOneRow()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.Open();
        conn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "Ada" }
        });

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"Id\", \"Name\" FROM \"Customers\"";
        var reader = (fakeDbDataReader)cmd.ExecuteReader();
        Assert.True(reader.Read());
        return reader;
    }

    [Fact]
    public void GetOrdinal_KnownColumn_ReturnsItsOrdinal()
    {
        using var reader = CreateReaderWithOneRow();

        Assert.Equal(0, reader.GetOrdinal("Id"));
        Assert.Equal(1, reader.GetOrdinal("Name"));
    }

    [Fact]
    public void GetOrdinal_UnknownColumn_ThrowsIndexOutOfRangeException()
    {
        using var reader = CreateReaderWithOneRow();

        var ex = Assert.Throws<System.IndexOutOfRangeException>(
            () => reader.GetOrdinal("this_column_definitely_does_not_exist_xyz123"));
        Assert.Contains("this_column_definitely_does_not_exist_xyz123", ex.Message);
    }
}
