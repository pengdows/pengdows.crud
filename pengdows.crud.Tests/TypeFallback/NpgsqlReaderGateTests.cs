using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// The typed Npgsql interval reader applies only to an Npgsql reader. A column another provider calls "interval" (SQLite's
/// declared INTERVAL, DuckDB's interval) must not be read as an NpgsqlInterval: the reader's type name is the test.
/// </summary>
public class NpgsqlReaderGateTests
{
    [Theory]
    [InlineData("Npgsql.NpgsqlDataReader", true)]
    [InlineData("NpgsqlTypes.NpgsqlInterval", true)]
    [InlineData("pengdows.crud.fakeDb.fakeDbDataReader", false)]
    [InlineData("DuckDB.NET.Data.DuckDBDataReader", false)]
    [InlineData("Microsoft.Data.Sqlite.SqliteDataReader", false)]
    [InlineData(null, false)]
    public void OnlyAnNpgsqlReaderIsNpgsql(string? readerTypeName, bool expected)
    {
        Assert.Equal(expected, NpgsqlTypedFieldReader.IsNpgsqlReaderTypeName(readerTypeName));
    }
}
