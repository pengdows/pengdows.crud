using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud;
using pengdows.crud.@internal;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Integration tests for INSERT with identity population across different database providers.
/// Tests both RETURNING-capable providers (SqlServer, PostgreSQL, SQLite, Firebird, Oracle)
/// and non-RETURNING providers (MySQL).
/// </summary>
[Collection("IntegrationTests")]
public class InsertReturningTests : DatabaseTestBase
{
    private const string TableName = "returning_test";

    // Providers that support RETURNING/OUTPUT clause
    private static readonly SupportedDatabase[] ReturningProviders =
    {
        SupportedDatabase.SqlServer,
        SupportedDatabase.PostgreSql,
        SupportedDatabase.Sqlite,
        SupportedDatabase.Firebird,
        SupportedDatabase.Oracle
    };

    // Providers that do NOT support RETURNING (INSERT works, but ID not populated inline)
    private static readonly SupportedDatabase[] NonReturningProviders =
    {
        SupportedDatabase.MySql
    };

    public InsertReturningTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        var allProviders = ReturningProviders.Concat(NonReturningProviders).ToArray();
        return IntegrationTestConfiguration.EnabledProviders
            .Where(p => allProviders.Contains(p))
            .ToList();
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await DropTableIfExistsAsync(context).ConfigureAwait(false);
        var createSql = GetCreateTableSql(provider, context);
        await using var container = context.CreateSqlContainer(createSql);
        await container.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task CreateAsync_ReturningClause_PopulatesIdentityAcrossProviders()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip non-RETURNING providers in this test
            if (NonReturningProviders.Contains(provider))
            {
                Output.WriteLine($"Skipping {provider} - does not support RETURNING clause");
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

    [Fact]
    public async Task CreateAsync_NonReturningProviders_InsertsSuccessfully()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Only test non-RETURNING providers
            if (!NonReturningProviders.Contains(provider))
            {
                return;
            }

            ((TypeMapRegistry)context.GetInternalTypeMapRegistry()).Register<ReturningEntity>();
            var helper = new TableGateway<ReturningEntity, long>(context);
            var uniqueName = $"noreturning-{provider}-{Guid.NewGuid():N}";
            var entity = new ReturningEntity
            {
                Name = uniqueName
            };

            var created = await helper.CreateAsync(entity, context);

            // INSERT should succeed
            Assert.True(created, $"INSERT should succeed for {provider}");

            // Row should exist in database (verify via raw SQL)
            await VerifyRowExistsAsync(context, uniqueName);

            // Note: ID may or may not be populated depending on provider's fallback mechanism
            Output.WriteLine($"{provider}: INSERT succeeded, ID = {entity.Id} (may be 0 if no RETURNING support)");
        });
    }

    [Fact]
    public async Task VerifyDialect_SupportsInsertReturning_MatchesExpectation()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var supportsReturning = context.SupportsInsertReturning;

            if (NonReturningProviders.Contains(provider))
            {
                Assert.False(supportsReturning,
                    $"{provider} should NOT support INSERT RETURNING");
            }
            else if (ReturningProviders.Contains(provider))
            {
                Assert.True(supportsReturning,
                    $"{provider} should support INSERT RETURNING");
            }

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
            SupportedDatabase.DuckDB => $@"
CREATE TABLE {table} (
    id BIGINT GENERATED BY DEFAULT AS IDENTITY,
    name TEXT NOT NULL
);",
            SupportedDatabase.Oracle => $@"
CREATE TABLE {table} (
    id NUMBER GENERATED BY DEFAULT ON NULL AS IDENTITY PRIMARY KEY,
    name VARCHAR2(255) NOT NULL
);",
            SupportedDatabase.MySql => $@"
CREATE TABLE {table} (
    `id` BIGINT AUTO_INCREMENT PRIMARY KEY,
    `name` VARCHAR(255) NOT NULL
);",
            _ => throw new NotSupportedException($"Provider {provider} is not supported by this test")
        };
    }

    private static async Task VerifyRowExistsAsync(IDatabaseContext context, string name)
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

        var count = Convert.ToInt32(await container.ExecuteScalarAsync<int>());
        Assert.Equal(1, count);
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
               || text.Contains("catalog error");
    }

    [Table(TableName)]
    private class ReturningEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }
}
