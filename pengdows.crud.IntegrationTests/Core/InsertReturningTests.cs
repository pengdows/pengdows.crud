using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud;
using pengdows.crud.@internal;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Integration tests for INSERT with identity population across different database providers.
/// Tests both RETURNING-capable providers (SqlServer, PostgreSQL, SQLite, Firebird, Oracle)
/// and non-RETURNING providers (MySQL).
/// </summary>
[Collection("IntegrationTests")]
public class InsertReturningTests : DatabaseTestBase
{
    private const string TableName = "returning_test";

    public InsertReturningTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    // Full reset before every test: its setup creates sequences, which TRUNCATE does not reset (SpannerSchemaReuse).
    protected override bool ReusesSchemaAcrossTests => false;

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await DropTableIfExistsAsync(context).ConfigureAwait(false);
        var createSql = GetCreateTableSql(provider, context);
        await using var container = context.CreateSqlContainer(createSql);
        await container.ExecuteNonQueryAsync();

        if (provider == SupportedDatabase.InterBase)
        {
            // InterBase has no identity columns: the gateway's PrefetchSequence plan reads
            // GEN_ID("<table>_seq", 1) before the INSERT, so that generator must exist. The
            // database is persistent (externally managed container), so drop a leftover first.
            var generator = context.WrapObjectName(TableName + "_seq");
            await using var drop = context.CreateSqlContainer($"DROP GENERATOR {generator}");
            try
            {
                await drop.ExecuteNonQueryAsync();
            }
            catch (Exception ex) when (ex.Message.Contains("Generator not found", StringComparison.OrdinalIgnoreCase))
            {
            }

            await using var create = context.CreateSqlContainer($"CREATE GENERATOR {generator}");
            await create.ExecuteNonQueryAsync();
        }
    }

    [SkippableFact]
    public async Task CreateAsync_ReturningClause_PopulatesIdentityAcrossProviders()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip non-RETURNING providers in this test
            if (!context.Dialect.SupportsInsertReturning)
            {
                Output.WriteLine($"Skipping {provider} - does not support INSERT ... RETURNING (covered by CreateAsync_NonReturningProviders_InsertsSuccessfully)");
                return;
            }

            ((TypeMapRegistry)context.GetInternalTypeMapRegistry()).Register<ReturningEntity>();
            var helper = new TableGateway<ReturningEntity, long>(context);
            var entity = new ReturningEntity
            {
                Name = $"returning-{provider}-{Guid.NewGuid():N}"
            };

            var created = await helper.CreateAsync(entity, context);
            Assert.True(created);
            Assert.True(entity.Id > 0, $"Expected ID > 0 for {provider}, got {entity.Id}");

            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);
            Assert.Equal(entity.Name, retrieved!.Name);

            await VerifyRowExistsAsync(context, entity.Name);

            Output.WriteLine($"{provider}: ID populated via RETURNING/OUTPUT = {entity.Id}");
        });
    }

    [SkippableFact]
    public async Task CreateAsync_NonReturningProviders_InsertsSuccessfully()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // The fallback path (no RETURNING) only exists where the dialect can't return the id.
            if (context.Dialect.SupportsInsertReturning)
            {
                Output.WriteLine(
                    $"[{provider}] Skipping: {provider} supports RETURNING/OUTPUT clause — identity population via RETURNING is covered in CreateAsync_ReturningClause_PopulatesIdentityAcrossProviders");
                return;
            }

            ((TypeMapRegistry)context.GetInternalTypeMapRegistry()).Register<ReturningEntity>();
            var helper = new TableGateway<ReturningEntity, long>(context);
            var uniqueName = $"noreturning-{provider}-{Guid.NewGuid():N}";
            var entity = new ReturningEntity
            {
                Name = uniqueName
            };

            // A dialect whose only generated-id mechanism is a [CorrelationToken] column refuses an
            // entity without one before writing (DEC-007); covered in detail by the Snowflake test.
            if (((pengdows.crud.dialects.SqlDialect)context.Dialect).RequiresCorrelationTokenForGeneratedIds)
            {
                await Assert.ThrowsAsync<NotSupportedException>(async () => await helper.CreateAsync(entity, context));
                await VerifyRowCountAsync(context, uniqueName, 0);
                return;
            }

            var created = await helper.CreateAsync(entity, context);

            // INSERT should succeed
            Assert.True(created, $"INSERT should succeed for {provider}");

            // Row should exist in database (verify via raw SQL)
            await VerifyRowExistsAsync(context, uniqueName);

            // Without RETURNING the id still comes back through the dialect's generated-key plan
            // (the insert's own reply, a session-scoped function, a compound statement, or the
            // correlation-token fallback), so it must be the new row's id.
            Assert.True(entity.Id > 0, $"{provider}: expected the generated id to be populated, got {entity.Id}");
            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);
            Assert.Equal(uniqueName, retrieved!.Name);

            Output.WriteLine($"{provider}: INSERT succeeded, ID = {entity.Id}");
        });
    }

    [SkippableFact]
    public async Task VerifyDialect_SupportsInsertReturning_MatchesExpectation()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var supportsReturning = context.SupportsInsertReturning;

            // Expected per database: whether pengdows uses RETURNING/OUTPUT (or Db2's FINAL TABLE)
            // to get the generated id. MariaDB added INSERT ... RETURNING in 10.5, so it follows
            // the detected server version.
            var serverVersion = ((pengdows.crud.dialects.SqlDialect)context.GetDialect()).ProductInfo.ParsedVersion;
            bool? expected = provider switch
            {
                SupportedDatabase.SqlServer or SupportedDatabase.PostgreSql or SupportedDatabase.Sqlite
                    or SupportedDatabase.Firebird or SupportedDatabase.Oracle or SupportedDatabase.YugabyteDb
                    or SupportedDatabase.CockroachDb or SupportedDatabase.DuckDB
                    or SupportedDatabase.Db2 or SupportedDatabase.Spanner => true,
                SupportedDatabase.MariaDb => serverVersion >= new Version(10, 5),
                SupportedDatabase.MySql or SupportedDatabase.TiDb
                    or SupportedDatabase.Snowflake or SupportedDatabase.FlatFile or SupportedDatabase.SingleStore
                    or SupportedDatabase.Informix or SupportedDatabase.SybaseASE
                    or SupportedDatabase.SapHana or SupportedDatabase.InterBase
                    or SupportedDatabase.Access => false,
                _ => null
            };

            Assert.True(expected.HasValue, $"{provider}: add its expected SupportsInsertReturning value to this test");
            Assert.Equal(expected, supportsReturning);

            Output.WriteLine($"{provider}: SupportsInsertReturning = {supportsReturning}");
            await Task.CompletedTask;
        });
    }

    private static string GetCreateTableSql(SupportedDatabase provider, IDatabaseContext context)
    {
        var table = context.WrapObjectName(TableName);

        return provider switch
        {
            SupportedDatabase.SqlServer => $@"
CREATE TABLE {table} (
    [id] INT IDENTITY(1,1) PRIMARY KEY,
    [name] NVARCHAR(255) NOT NULL
);",
            SupportedDatabase.PostgreSql => $@"
CREATE TABLE {table} (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name VARCHAR(255) NOT NULL
);",
            SupportedDatabase.Sqlite => $@"
CREATE TABLE {table} (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL
);",
            SupportedDatabase.Firebird => $@"
CREATE TABLE {table} (
    ""id"" BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    ""name"" VARCHAR(255) NOT NULL
);",
            // DuckDB has no identity columns; the id defaults from a sequence. The DROP runs first
            // because SetupDatabaseAsync only drops the table.
            SupportedDatabase.DuckDB => $@"
DROP SEQUENCE IF EXISTS {context.WrapObjectName(TableName + "_seq")};
CREATE SEQUENCE {context.WrapObjectName(TableName + "_seq")};
CREATE TABLE {table} (
    {context.WrapObjectName("id")} BIGINT DEFAULT nextval('{TableName}_seq') PRIMARY KEY,
    {context.WrapObjectName("name")} TEXT NOT NULL
);",
            SupportedDatabase.Oracle => $@"
CREATE TABLE {table} (
    {context.WrapObjectName("id")} NUMBER GENERATED BY DEFAULT ON NULL AS IDENTITY PRIMARY KEY,
    {context.WrapObjectName("name")} VARCHAR2(255) NOT NULL
);",
            SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb or SupportedDatabase.SingleStore => $@"
CREATE TABLE {table} (
    `id` BIGINT AUTO_INCREMENT PRIMARY KEY,
    `name` VARCHAR(255) NOT NULL
);",
            SupportedDatabase.YugabyteDb or SupportedDatabase.CockroachDb => $@"
CREATE TABLE {table} (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name VARCHAR(255) NOT NULL
);",
            SupportedDatabase.Snowflake => $@"
CREATE TABLE {table} (
    {context.WrapObjectName("id")} BIGINT AUTOINCREMENT PRIMARY KEY,
    {context.WrapObjectName("name")} VARCHAR(255) NOT NULL
)",
            // ISO SQL: no IDENTITY column, so the id defaults from a sequence. The DROP runs first
            // because SetupDatabaseAsync only drops the table.
            SupportedDatabase.FlatFile => $@"
DROP SEQUENCE IF EXISTS {context.WrapObjectName(TableName + "_seq")};
CREATE SEQUENCE {context.WrapObjectName(TableName + "_seq")};
CREATE TABLE {table} (
    {context.WrapObjectName("id")} BIGINT DEFAULT NEXT VALUE FOR {context.WrapObjectName(TableName + "_seq")} NOT NULL PRIMARY KEY,
    {context.WrapObjectName("name")} VARCHAR(255) NOT NULL
)",
            SupportedDatabase.Db2 => $@"
CREATE TABLE {table} (
    {context.WrapObjectName("id")} BIGINT NOT NULL GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    {context.WrapObjectName("name")} VARCHAR(255) NOT NULL
)",
            // Spanner supports only GENERATED BY DEFAULT identity columns.
            SupportedDatabase.Spanner => $@"
CREATE TABLE {table} (
    {context.WrapObjectName("id")} BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    {context.WrapObjectName("name")} VARCHAR(255) NOT NULL
)",
            SupportedDatabase.Informix => $@"
CREATE TABLE {table} (
    {context.WrapObjectName("id")} BIGSERIAL NOT NULL PRIMARY KEY,
    {context.WrapObjectName("name")} VARCHAR(255) NOT NULL
)",
            SupportedDatabase.SybaseASE => $@"
CREATE TABLE {table} (
    {context.WrapObjectName("id")} BIGINT IDENTITY NOT NULL PRIMARY KEY,
    {context.WrapObjectName("name")} VARCHAR(255) NOT NULL
)",
            SupportedDatabase.SapHana => $@"
CREATE COLUMN TABLE {table} (
    {context.WrapObjectName("id")} BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    {context.WrapObjectName("name")} NVARCHAR(255) NOT NULL
)",
            // No identity columns; the id comes from the "<table>_seq" generator created in
            // SetupDatabaseAsync.
            SupportedDatabase.InterBase => $@"
CREATE TABLE {table} (
    {context.WrapObjectName("id")} NUMERIC(18, 0) NOT NULL PRIMARY KEY,
    {context.WrapObjectName("name")} VARCHAR(255) NOT NULL
)",
            // Access: COUNTER is the AutoNumber column; the id comes back through SELECT @@IDENTITY
            // (GeneratedKeyPlan.SessionScopedFunction), there is no RETURNING.
            SupportedDatabase.Access => $@"
CREATE TABLE {table} (
    {context.WrapObjectName("id")} COUNTER NOT NULL PRIMARY KEY,
    {context.WrapObjectName("name")} TEXT(255) NOT NULL
)",
            _ => throw new NotSupportedException($"Provider {provider} is not supported by this test")
        };
    }

    private static Task VerifyRowExistsAsync(IDatabaseContext context, string name) =>
        VerifyRowCountAsync(context, name, 1);

    private static async Task VerifyRowCountAsync(IDatabaseContext context, string name, int expected)
    {
        var table = context.WrapObjectName(TableName);
        var nameColumn = context.Product == SupportedDatabase.Firebird
            ? "\"name\""
            : context.WrapObjectName("name");

        await using var container = context.CreateSqlContainer($@"
SELECT COUNT(1)
FROM {table}
WHERE {nameColumn} = ");

        var parameterName = container.MakeParameterName("p0");
        container.Query.Append(parameterName);
        container.AddParameterWithValue("p0", DbType.String, name);

        var count = Convert.ToInt32(await container.ExecuteScalarOrNullAsync<int>());
        Assert.Equal(expected, count);
    }

    private static async Task DropTableIfExistsAsync(IDatabaseContext context)
    {
        var dropSql = $"DROP TABLE {context.WrapObjectName(TableName)}";
        await using var container = context.CreateSqlContainer(dropSql);
        try
        {
            await container.ExecuteNonQueryAsync();
        }
        catch (Exception ex) when (IsTableMissing(ex.Message))
        {
            // ignore
        }
    }

    private static bool IsTableMissing(string? message)
    {
        var text = message?.ToLowerInvariant() ?? string.Empty;
        return text.Contains("does not exist")
               || text.Contains("doesn't exist")
               || text.Contains("no such table")
               || text.Contains("unknown table")
               || text.Contains("table not found")
               || text.Contains("invalid object name")
               || text.Contains("ora-00942")
               || text.Contains("table unknown")
               || text.Contains("table with name")
               || text.Contains("catalog error")
               // Db2 SQL0204N: <schema>.<name> is an undefined name (raised on DROP TABLE for a
               // table that was never created — expected for providers only some tests exercise).
               || text.Contains("sql0204n")
               || text.Contains("is an undefined name")
               || text.Contains("is not in the database")
               // SAP HANA (error 259): "invalid table name: <name>"
               || text.Contains("invalid table name");
    }

    /// <summary>
    /// Snowflake-specific: verifies AUTOINCREMENT identity columns work for INSERT, and that
    /// rows can be verified via name lookup. Snowflake does not support INSERT...RETURNING;
    /// ID population uses LAST_INSERT_ID() on a best-effort basis (connection-scoped).
    /// </summary>
    [SkippableFact]
    public async Task Snowflake_AutoIncrement_WithoutCorrelationToken_RefusesBeforeWriting()
    {
        // DEC-007: Snowflake has no RETURNING, sequence prefetch or last-id function, so without a
        // [CorrelationToken] column the id could never be read back. CreateAsync refuses with a
        // NotSupportedException naming the fix, and nothing is written.
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.Snowflake }, async (provider, context) =>
        {
            ((TypeMapRegistry)context.GetInternalTypeMapRegistry()).Register<ReturningEntity>();
            var helper = new TableGateway<ReturningEntity, long>(context);
            var entity = new ReturningEntity { Name = $"sf-autoincrement-{Guid.NewGuid():N}" };

            var ex = await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await helper.CreateAsync(entity, context));

            Assert.Contains("[CorrelationToken]", ex.Message, StringComparison.Ordinal);
            await VerifyRowCountAsync(context, entity.Name, 0);
        });
    }

    [Table(TableName)]
    private class ReturningEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }
}
