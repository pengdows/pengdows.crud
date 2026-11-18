using DuckDB.NET.Data;
using pengdows.crud.@internal;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.@internal
{
    /// <summary>
    /// DuckDB's own interval column, read through a real in-memory DuckDB reader (DuckDB.NET is an embedded engine, so
    /// no server is needed). The typed path depends on the engine's reader type and its INTERVAL column.
    /// </summary>
    public class DuckDbIntervalReaderTests
    {
        private static DuckDBDataReader Query(DuckDBConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var reader = (DuckDBDataReader)command.ExecuteReader();
            Assert.True(reader.Read());
            return reader;
        }

        [Fact]
        public void ReadValue_IntervalColumn_ReadsTheEngineIntervalAsPostgreSqlInterval()
        {
            using var connection = new DuckDBConnection("DataSource=:memory:");
            connection.Open();
            using var reader = Query(connection, "SELECT INTERVAL '2 days 3 seconds' AS v");

            var result = Assert.IsType<PostgreSqlInterval>(DuckDbIntervalReader.ReadValue(reader, 0));

            Assert.Equal(0, result.Months);
            Assert.Equal(2, result.Days);
            Assert.Equal(3_000_000L, result.Microseconds);
        }

        [Fact]
        public void ReadValue_NonIntervalColumn_ReadsThroughGetValue()
        {
            using var connection = new DuckDBConnection("DataSource=:memory:");
            connection.Open();
            using var reader = Query(connection, "SELECT 42 AS v");

            Assert.Equal(42, DuckDbIntervalReader.ReadValue(reader, 0));
        }
    }
}
