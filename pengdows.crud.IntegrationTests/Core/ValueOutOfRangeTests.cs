using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// TYPE-008: a stored value the property type can't hold must throw DataMappingException, never
/// truncate or default. Each database stores 9e28 (above decimal.MaxValue, within every engine's
/// widest exact numeric) and the gateway reads it into a decimal property. A database that can't
/// store the value at all (FlatFile's DECIMAL is System.Decimal) rejects it at INSERT instead,
/// which is equally never a wrong value.
/// </summary>
[Collection("IntegrationTests")]
public class ValueOutOfRangeTests : DatabaseTestBase
{
    private const string TableName = "value_out_of_range";
    private const string AboveDecimalMax = "90000000000000000000000000000";

    public ValueOutOfRangeTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    private static string WideNumeric(SupportedDatabase provider) => provider switch
    {
        SupportedDatabase.Oracle => "NUMBER(38)",
        SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb
            or SupportedDatabase.SingleStore => "DECIMAL(65,0)",
        SupportedDatabase.Informix => "DECIMAL(32,0)",
        // Db2's maximum DECIMAL precision is 31 (SQL0604N on 38); 31 digits still exceed decimal.
        SupportedDatabase.Db2 => "DECIMAL(31,0)",
        SupportedDatabase.Sqlite or SupportedDatabase.Spanner => "NUMERIC",
        // InterBase's exact numerics stop at 18 digits, below decimal's range; a double holds 9e28
        // and the read must still overflow loudly (HARN-011).
        SupportedDatabase.InterBase => "DOUBLE PRECISION",
        _ => "DECIMAL(38,0)"
    };

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var w = (string name) => context.WrapObjectName(name);
        var idType = provider == SupportedDatabase.Spanner ? "BIGINT" : "INTEGER";
        // A database kept between runs (InterBase's externally managed container) still has the
        // table from the last run (HARN-011).
        await DropTableIfExistsAsync(context, TableName);
        await using var sc = context.CreateSqlContainer(
            $"CREATE TABLE {IntegrationObjectNameHelper.Table(context, TableName)} (" +
            $"{w("id")} {idType} NOT NULL PRIMARY KEY, {w("amount")} {WideNumeric(provider)})");
        await sc.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task DecimalAboveItsRange_ThrowsDataMappingException_NeverAWrongValue()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var table = IntegrationObjectNameHelper.Table(context, TableName);
            try
            {
                await using var insert = context.CreateSqlContainer(
                    $"INSERT INTO {table} ({context.WrapObjectName("id")}, {context.WrapObjectName("amount")}) " +
                    $"VALUES (1, {AboveDecimalMax})");
                await insert.ExecuteNonQueryAsync();
            }
            catch (Exception ex) when (ex is DatabaseException or OverflowException)
            {
                Output.WriteLine($"[{provider}] rejected at INSERT: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            var gateway = new TableGateway<Account, int>(context);
            var mapping = await Assert.ThrowsAsync<DataMappingException>(async () =>
                await gateway.RetrieveOneAsync(1, context));

            // A provider that decodes the whole row inside Read or ExecuteReader (FirebirdClient,
            // AdoNetCore.AseClient) fails before any column is identified; everywhere else the
            // message names the column.
            Assert.True(mapping.Message.Contains("amount", StringComparison.Ordinal) ||
                        mapping.Message.StartsWith("Could not read the", StringComparison.Ordinal),
                mapping.Message);
            Output.WriteLine($"[{provider}] {mapping.Message} (inner {mapping.InnerException?.GetType().Name})");
        });
    }

    [Table(TableName)]
    public sealed class Account
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("amount", DbType.Decimal)] public decimal Amount { get; set; }
    }
}
