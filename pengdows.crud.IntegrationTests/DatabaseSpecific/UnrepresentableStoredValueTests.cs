using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// TYPE-005: stored values with no .NET representation — a MySQL zero date, a SQL Server
/// geometry without Microsoft.SqlServer.Types, a PostgreSQL numeric NaN — surface as a
/// DataMappingException naming the column, never a raw provider exception or a default value.
/// (hierarchyid, the first SQL Server case, reads as HierarchyId since TYPE-016.)
/// </summary>
[Collection("IntegrationTests")]
public sealed class UnrepresentableStoredValueTests : DatabaseTestBase
{
    private const string TableName = "unrepresentable_values";

    public UnrepresentableStoredValueTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture) { }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        [SupportedDatabase.MySql, SupportedDatabase.SqlServer, SupportedDatabase.PostgreSql];

    private static (string Ddl, string Literal) Case(SupportedDatabase provider) => provider switch
    {
        SupportedDatabase.MySql => ("DATETIME", "'0000-00-00 00:00:00'"),
        SupportedDatabase.SqlServer => ("geometry", "geometry::Parse('POINT(1 2)')"),
        _ => ("NUMERIC", "'NaN'")
    };

    // Full reset before every test: its setup inserts the legacy rows the tests read (SpannerSchemaReuse).
    protected override bool ReusesSchemaAcrossTests => false;

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var (ddl, literal) = Case(provider);
        var table = IntegrationObjectNameHelper.Table(context, TableName);
        await using (var create = context.CreateSqlContainer(
                         $"CREATE TABLE {table} ({context.WrapObjectName("id")} INTEGER NOT NULL PRIMARY KEY, " +
                         $"{context.WrapObjectName("v")} {ddl} NULL)"))
        {
            await create.ExecuteNonQueryAsync();
        }

        // MySQL's default sql_mode rejects a zero date on INSERT; the row models legacy data.
        await using var insert = context.CreateSqlContainer(
            (provider == SupportedDatabase.MySql ? "INSERT IGNORE" : "INSERT") +
            $" INTO {table} ({context.WrapObjectName("id")}, {context.WrapObjectName("v")}) VALUES (1, {literal})");
        await insert.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task UnrepresentableValue_ThrowsDataMappingException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            DataMappingException ex = provider switch
            {
                SupportedDatabase.MySql => await Assert.ThrowsAsync<DataMappingException>(
                    async () => await new TableGateway<ZeroDateRow, int>(context).RetrieveOneAsync(1, context)),
                SupportedDatabase.SqlServer => await Assert.ThrowsAsync<DataMappingException>(
                    async () => await new TableGateway<GeometryRow, int>(context).RetrieveOneAsync(1, context)),
                _ => await Assert.ThrowsAsync<DataMappingException>(
                    async () => await new TableGateway<NaNRow, int>(context).RetrieveOneAsync(1, context)),
            };
            Assert.Contains("'v'", ex.Message);
        });
    }

    [Table(TableName)]
    internal sealed class ZeroDateRow
    {
        [Id][Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.DateTime)] public DateTime? V { get; set; }
    }

    [Table(TableName)]
    internal sealed class GeometryRow
    {
        [Id][Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.String)] public string? V { get; set; }
    }

    [Table(TableName)]
    internal sealed class NaNRow
    {
        [Id][Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Decimal)] public decimal? V { get; set; }
    }
}
