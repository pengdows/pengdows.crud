#region

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Access's Date/Time Extended (DATETIME2, 100 ns) is stored correctly but cannot be read through OLE DB: the provider reports it
/// as DBTYPE_DBTIMESTAMP and returns the month one early (Jun reads May, Dec 31 reads Nov 31, Jan reads month 0), and a read can
/// crash the process (verified live; docs/datatype-live-crossref.md). A plain DATETIME is DBTYPE_DATE and reads correctly. A
/// result set holding such a column is refused when its reader opens, before any value is read, by name, so the caller gets a
/// typed error and never a wrong date.
/// </summary>
public class AccessExtendedDateTimeRefusalTests
{
    private static (IDatabaseContext Context, fakeDbConnection Connection) ContextReturning(
        SupportedDatabase database, string columnName, string dataTypeName)
    {
        var factory = new fakeDbFactory(database);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={database}", factory);
        var connection = new fakeDbConnection { EmulatedProduct = database };
        connection.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { [columnName] = new DateTime(2026, 10, 5, 13, 45, 30) }
        })
        {
            Columns = new[] { new fakeDbColumn(columnName, typeof(DateTime), dataTypeName) }
        });
        factory.Connections.Add(connection);
        return (context, connection);
    }

    [Fact]
    public async Task AccessResultWithAnExtendedDateTimeColumn_IsRefusedWhenTheReaderOpens_ByColumnName()
    {
        var (context, _) = ContextReturning(SupportedDatabase.Access, "stamped_at", "DBTYPE_DBTIMESTAMP");
        await using var _ = context;
        await using var sc = context.CreateSqlContainer("SELECT stamped_at FROM events");

        var ex = await Assert.ThrowsAsync<DataMappingException>(async () =>
        {
            await using var reader = await sc.ExecuteReaderAsync();
        });

        Assert.Contains("stamped_at", ex.Message);
        Assert.Contains("Date/Time Extended", ex.Message);
    }

    [Fact]
    public async Task AccessRefusal_ReleasesTheConnection()
    {
        var (context, _) = ContextReturning(SupportedDatabase.Access, "stamped_at", "DBTYPE_DBTIMESTAMP");
        await using var _ = context;
        await using var sc = context.CreateSqlContainer("SELECT stamped_at FROM events");

        await Assert.ThrowsAsync<DataMappingException>(async () =>
        {
            await using var reader = await sc.ExecuteReaderAsync();
        });

        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Fact]
    public async Task AccessPlainDateTimeColumn_ReadsAsBefore()
    {
        var (context, _) = ContextReturning(SupportedDatabase.Access, "stamped_at", "DBTYPE_DATE");
        await using var _ = context;
        await using var sc = context.CreateSqlContainer("SELECT stamped_at FROM events");

        await using var reader = await sc.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(new DateTime(2026, 10, 5, 13, 45, 30), reader.GetDateTime(0));
    }

    [Fact]
    public async Task AnotherDatabase_ReportingTheSameTypeName_IsNotRefused()
    {
        var (context, _) = ContextReturning(SupportedDatabase.PostgreSql, "stamped_at", "DBTYPE_DBTIMESTAMP");
        await using var _ = context;
        await using var sc = context.CreateSqlContainer("SELECT stamped_at FROM events");

        await using var reader = await sc.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
    }
}
