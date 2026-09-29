using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CONFIRMED live (Firebird 5, PreventDatabaseUnload): the data sources were created before
/// InitializePoolGovernors added PreventDatabaseUnload's provider minimum (MinPoolSize=2) to the
/// context's connection strings. Real connections therefore came from a pool keyed by the
/// unmodified string, while everything that manages pools by string - the Firebird DDL pool reset
/// in particular - used the modified one. The reset cleared a pool nobody used, a stale reader
/// attachment kept the table in use, and DROP/CREATE failed with "object TABLE ... is in use".
/// The connection string a context reports for each pool must be the one its connections open with.
/// </summary>
public class PoolConnectionStringConsistencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreventDatabaseUnload_ConnectionsOpenWithTheContextsPoolConnectionStrings(bool nativeDataSource)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird) { SupportsNativeDataSource = nativeDataSource };
        using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=localhost;Database=/data/test.fdb;EmulatedProduct=Firebird",
            DbMode = DbMode.PreventDatabaseUnload,
            ReadWriteMode = ReadWriteMode.ReadWrite
        }, factory);

        await using (var write = context.CreateSqlContainer("UPDATE t SET x = 1"))
        {
            await write.ExecuteNonQueryAsync();
        }

        await using (var read = context.CreateSqlContainer("SELECT 1"))
        {
            await read.ExecuteScalarOrNullAsync<int>(ExecutionType.Read);
        }

        var writer = context.RawConnectionString;
        var reader = context.RawReaderConnectionString;
        Assert.Contains("MinPoolSize=2", writer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MinPoolSize=2", reader, StringComparison.OrdinalIgnoreCase);

        // The first connection is the construction-time detection connection, opened with the
        // caller's raw string before pool configuration exists. Under PreventDatabaseUnload it is
        // kept as the writer sentinel, which never runs work or touches tables. Every connection
        // used for work must use a managed pool string.
        var opened = factory.CreatedConnections.Skip(1).Select(c => c.ConnectionString).ToList();
        Assert.Contains(writer, opened);
        Assert.Contains(reader, opened);
        Assert.All(opened, cs => Assert.True(cs == writer || cs == reader,
            $"Connection opened with a string the context does not manage:\n{cs}\nwriter: {writer}\nreader: {reader}"));
    }
}
