using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Proves, against real live databases, that a column identifier containing a genuinely
/// embedded, unescaped quote character round-trips correctly through
/// <c>WrapObjectName</c>/<c>WrapSimpleName</c>'s escaping logic — the exact class of identifier
/// <c>QuotingTortureTests</c> never exercised (it only covers reserved words, spaces, and mixed
/// case). This is the live counterpart to the unit-level
/// <c>SqlDialectAdditionalCoverageTests.WrapObjectName_SegmentWithValidlyEscapedInnerQuotes_IsLeftAlone</c>
/// and <c>..._SegmentLooksPreWrappedButHasUnescapedInnerQuote_IsSafelyEscaped</c> tests: those
/// prove the escaping logic produces the right SQL text in isolation, this proves a real engine
/// actually accepts and round-trips that generated DDL/DML correctly end to end.
/// </summary>
[Collection("IntegrationTests")]
public class EmbeddedQuoteIdentifierIntegrationTests : IAsyncLifetime
{
    private const string TableName = "quote_torture_probe";
    private const string ColumnName = "Value \"Quoted\" Name";

    private readonly ITestOutputHelper _output;
    private readonly IntegrationTestFixture _fixture;
    private IDatabaseContext? _sqlite;
    private IDatabaseContext? _postgres;

    public EmbeddedQuoteIdentifierIntegrationTests(ITestOutputHelper output, IntegrationTestFixture fixture)
    {
        _output = output;
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        if (IntegrationTestConfiguration.EnabledProviders.Contains(SupportedDatabase.Sqlite))
        {
            _sqlite = await _fixture.CreateAdditionalContextAsync(SupportedDatabase.Sqlite);
            await using var create = _sqlite.CreateSqlContainer($@"
CREATE TABLE IF NOT EXISTS {TableName} (
    id INTEGER PRIMARY KEY,
    {_sqlite.WrapObjectName(ColumnName)} TEXT
)");
            await create.ExecuteNonQueryAsync();
        }

        if (IntegrationTestConfiguration.EnabledProviders.Contains(SupportedDatabase.PostgreSql))
        {
            _postgres = await _fixture.CreateAdditionalContextAsync(SupportedDatabase.PostgreSql);
            await using var create = _postgres.CreateSqlContainer($@"
CREATE TABLE IF NOT EXISTS {_postgres.WrapObjectName(TableName)} (
    id BIGINT PRIMARY KEY,
    {_postgres.WrapObjectName(ColumnName)} VARCHAR(255)
)");
            await create.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_sqlite != null)
        {
            await using var drop = _sqlite.CreateSqlContainer($"DROP TABLE IF EXISTS {TableName}");
            await drop.ExecuteNonQueryAsync();
            _sqlite.Dispose();
        }

        if (_postgres != null)
        {
            await using var drop = _postgres.CreateSqlContainer($"DROP TABLE IF EXISTS {_postgres.WrapObjectName(TableName)}");
            await drop.ExecuteNonQueryAsync();
            _postgres.Dispose();
        }
    }

    [SkippableFact]
    public async Task SqliteAndPostgres_ColumnNameWithEmbeddedQuote_RoundTripsCorrectly()
    {
        Skip.If(_sqlite == null && _postgres == null,
            "Neither SQLite nor PostgreSQL is enabled for this test run.");

        if (_sqlite != null)
        {
            await RoundTripAsync(_sqlite, "sqlite-value");
        }

        if (_postgres != null)
        {
            await RoundTripAsync(_postgres, "postgres-value");
        }
    }

    private static async Task RoundTripAsync(IDatabaseContext context, string value)
    {
        var gateway = new TableGateway<EmbeddedQuoteEntity, long>(context);
        var entity = new EmbeddedQuoteEntity { Id = DateTime.UtcNow.Ticks, Value = value };

        await gateway.CreateAsync(entity, context);
        var retrieved = await gateway.RetrieveOneAsync(entity.Id, context);

        Assert.NotNull(retrieved);
        Assert.Equal(value, retrieved!.Value);
    }

    [Table(TableName)]
    private class EmbeddedQuoteEntity
    {
        [Id]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column(ColumnName, DbType.String)]
        public string Value { get; set; } = string.Empty;
    }
}
