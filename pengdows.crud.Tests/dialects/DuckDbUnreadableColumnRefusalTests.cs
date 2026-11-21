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
/// DuckDB.NET reports a UNION column's type name ("Union") and a spatial GEOMETRY column's ("Geometry"), then throws a raw
/// ArgumentException ("Unrecognised type Union (28) for column v") from GetFieldType and GetValue (verified live, DuckDB.NET
/// 2.0.1). A result set holding one is refused when its reader opens, before any value is read, by column name, with a typed
/// DataMappingException that says what to select instead. A STRUCT, MAP or LIST column reads as before.
/// </summary>
public class DuckDbUnreadableColumnRefusalTests
{
    private static (IDatabaseContext Context, fakeDbConnection Connection) ContextReturning(
        SupportedDatabase database, string columnName, string dataTypeName)
    {
        var factory = new fakeDbFactory(database);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={database}", factory);
        var connection = new fakeDbConnection { EmulatedProduct = database };
        connection.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { [columnName] = "stored" }
        })
        {
            Columns = new[] { new fakeDbColumn(columnName, typeof(object), dataTypeName) }
        });
        factory.Connections.Add(connection);
        return (context, connection);
    }

    [Theory]
    [InlineData("Union", "UNION")]
    [InlineData("Geometry", "ST_AsText")]
    public async Task DuckDbResultWithAnUnreadableColumn_IsRefusedWhenTheReaderOpens_ByColumnName(
        string dataTypeName, string advice)
    {
        var (context, _) = ContextReturning(SupportedDatabase.DuckDB, "payload", dataTypeName);
        await using var _ = context;
        await using var sc = context.CreateSqlContainer("SELECT payload FROM events");

        var ex = await Assert.ThrowsAsync<DataMappingException>(async () =>
        {
            await using var reader = await sc.ExecuteReaderAsync();
        });

        Assert.Contains("payload", ex.Message);
        Assert.Contains(advice, ex.Message);
        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Fact]
    public async Task DuckDbScalarReadOfAnUnreadableColumn_IsRefusedToo()
    {
        var (context, _) = ContextReturning(SupportedDatabase.DuckDB, "payload", "Union");
        await using var _ = context;
        await using var sc = context.CreateSqlContainer("SELECT payload FROM events");

        await Assert.ThrowsAsync<DataMappingException>(async () => await sc.ExecuteScalarRequiredAsync<object>());

        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Theory]
    [InlineData("Struct")]
    [InlineData("Map")]
    [InlineData("List")]
    [InlineData("Array")]
    public async Task DuckDbStructMapAndListColumns_ReadAsBefore(string dataTypeName)
    {
        var (context, _) = ContextReturning(SupportedDatabase.DuckDB, "payload", dataTypeName);
        await using var _ = context;
        await using var sc = context.CreateSqlContainer("SELECT payload FROM events");

        await using var reader = await sc.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
    }

    [Fact]
    public async Task AnotherDatabase_ReportingTheSameTypeName_IsNotRefused()
    {
        var (context, _) = ContextReturning(SupportedDatabase.PostgreSql, "payload", "Union");
        await using var _ = context;
        await using var sc = context.CreateSqlContainer("SELECT payload FROM events");

        await using var reader = await sc.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
    }
}
