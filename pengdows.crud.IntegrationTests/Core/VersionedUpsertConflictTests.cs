using System.Data;
using pengdows.crud.@internal;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Integration tests proving, against REAL database instances, that the
/// <c>ISqlDialect.EmitsAnsiMergeSyntax</c> capability flag correctly gates optimistic-concurrency
/// conflict detection inside <c>TableGateway.UpsertAsync</c>.
/// <para>
/// Before this flag existed, the gateway used a hardcoded
/// <c>ctx.DataSourceInfo.Product != SupportedDatabase.Firebird</c> check to decide whether a
/// 0-rows-affected MERGE-based upsert meant "version conflict". <c>fakeDb</c> never parses SQL or
/// executes MERGE, so it cannot prove the real conflict-detection behavior on either side of that
/// flag — these tests exercise a real PostgreSQL 15+ engine (which really does support
/// <c>MERGE ... WHEN MATCHED</c>) and a real Firebird engine (whose <c>UPDATE OR INSERT MATCHING</c>
/// genuinely cannot detect the conflict) to confirm the flag reflects reality.
/// </para>
/// </summary>
[Collection("IntegrationTests")]
public class VersionedUpsertConflictTests : DatabaseTestBase
{
    private const string TableName = "versioned_upsert_entities";

    private static readonly SupportedDatabase[] TargetProviders =
    {
        SupportedDatabase.PostgreSql,
        SupportedDatabase.DuckDB,
        SupportedDatabase.Firebird
    };

    public VersionedUpsertConflictTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return base.GetSupportedProviders().Where(TargetProviders.Contains).ToList();
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        context.RegisterEntity<VersionedUpsertEntity>();
        await DropTableIfExistsAsync(context, TableName);
        await using var container = context.CreateSqlContainer(BuildTableSql(provider, context));
        await container.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// PostgreSQL 15+ satisfies <c>SupportsMerge &amp;&amp; EmitsAnsiMergeSyntax</c>, so a
    /// stale-version <c>UpsertAsync</c> against a row that was concurrently modified must be
    /// detected via real ANSI <c>MERGE ... WHEN MATCHED AND t.version = s.version</c> semantics
    /// (0 rows affected on mismatch) and surfaced as <see cref="ConcurrencyConflictException"/>.
    /// </summary>
    [SkippableFact]
    public Task UpsertAsync_StaleVersion_MergeCapableProvider_ThrowsConcurrencyConflict()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (provider == SupportedDatabase.Firebird)
            {
                Output.WriteLine(
                    $"{provider}: skipped here — Firebird's non-detection behavior is asserted by " +
                    $"{nameof(UpsertAsync_StaleVersion_Firebird_DoesNotThrow)}.");
                return;
            }

            var dialect = context.Dialect;
            Output.WriteLine(
                $"{provider}: SupportsMerge={dialect.SupportsMerge}, EmitsAnsiMergeSyntax={dialect.EmitsAnsiMergeSyntax}, ProductVersion={dialect.ProductInfo.ProductVersion}");

            if (provider == SupportedDatabase.DuckDB && !(dialect.SupportsMerge && dialect.EmitsAnsiMergeSyntax))
            {
                Output.WriteLine(
                    $"{provider}: SKIPPED — SupportsMerge requires DuckDB >= 1.4.0. The pinned " +
                    "DuckDB.NET.Data.Full 1.3.2 package (testbed.csproj) bundles an older native DuckDB " +
                    "build, so this dialect falls back to INSERT ... ON CONFLICT here instead of MERGE, " +
                    "which means this test cannot exercise the EmitsAnsiMergeSyntax/MERGE path for DuckDB " +
                    "in this environment. Bumping the package to 1.4.3 was tried and confirmed to flip " +
                    "SupportsMerge on, but it also surfaces an unrelated real regression in DuckDB's " +
                    "ON CONFLICT DO UPDATE alias binding (\"Referenced table \\\"s\\\" not found\") that " +
                    "pre-dates this task and needs its own fix in DuckDbDialect before the package can be " +
                    "safely upgraded. See docs/FUTURE_WORK.md.");
                return;
            }

            Assert.True(dialect.SupportsMerge && dialect.EmitsAnsiMergeSyntax,
                $"{provider} was expected to exercise the ANSI MERGE conflict-detection path for this test.");

            var helper = new TableGateway<VersionedUpsertEntity, long>(context);
            var initial = new VersionedUpsertEntity { Id = 1, Name = "original", Version = 1 };
            await helper.CreateAsync(initial, context);

            // Two independent "concurrent" holders of the same row, both still at version 1.
            var holderA = await helper.RetrieveOneAsync(initial.Id, context);
            var holderB = await helper.RetrieveOneAsync(initial.Id, context);
            Assert.NotNull(holderA);
            Assert.NotNull(holderB);

            holderA!.Name = "updated-by-a";
            var firstUpsert = await helper.UpsertAsync(holderA, context);
            Assert.Equal(1, firstUpsert);

            // holderB still thinks the version is 1; the real row is now at version 2.
            holderB!.Name = "updated-by-b";
            await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
                await helper.UpsertAsync(holderB, context));

            var final = await helper.RetrieveOneAsync(initial.Id, context);
            Assert.NotNull(final);
            Assert.Equal("updated-by-a", final!.Name);
            Output.WriteLine($"{provider}: final name '{final.Name}' at version {final.Version} (holderB's stale upsert correctly rejected)");
        });
    }

    /// <summary>
    /// Firebird's <c>UPDATE OR INSERT ... MATCHING</c> has no WHEN-MATCHED-style version predicate,
    /// so a stale-version upsert cannot be detected and must NOT throw
    /// <see cref="ConcurrencyConflictException"/> — this is documented, expected behavior
    /// (<see cref="pengdows.crud.dialects.ISqlDialect.EmitsAnsiMergeSyntax"/> is false for Firebird),
    /// not a bug.
    /// </summary>
    [SkippableFact]
    public Task UpsertAsync_StaleVersion_Firebird_DoesNotThrow()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (provider != SupportedDatabase.Firebird)
            {
                Output.WriteLine($"{provider}: skipped — this test targets Firebird's specific non-detection behavior.");
                return;
            }

            Assert.True(context.Dialect.SupportsMerge, "Firebird 2+ is expected to satisfy SupportsMerge.");
            Assert.False(context.Dialect.EmitsAnsiMergeSyntax,
                "Firebird satisfies SupportsMerge via UPDATE OR INSERT MATCHING, not real ANSI MERGE.");

            var helper = new TableGateway<VersionedUpsertEntity, long>(context);
            var initial = new VersionedUpsertEntity { Id = 2, Name = "original", Version = 1 };
            await helper.CreateAsync(initial, context);

            var holderA = await helper.RetrieveOneAsync(initial.Id, context);
            var holderB = await helper.RetrieveOneAsync(initial.Id, context);

            holderA!.Name = "updated-by-a";
            await helper.UpsertAsync(holderA, context);

            // holderB is stale (still version 1 in memory); Firebird's UPDATE OR INSERT MATCHING
            // cannot detect this and must silently succeed rather than throw.
            holderB!.Name = "updated-by-b";
            var exception = await Record.ExceptionAsync(async () => await helper.UpsertAsync(holderB, context));

            Assert.Null(exception);
            Output.WriteLine($"{provider}: stale-version upsert did not throw (documented limitation, not a bug)");
        });
    }

    private static string BuildTableSql(SupportedDatabase provider, IDatabaseContext context)
    {
        var table = IntegrationObjectNameHelper.Table(context, TableName);
        var idColumn = context.WrapObjectName("id");
        var nameColumn = context.WrapObjectName("name");
        var versionColumn = context.WrapObjectName("version");

        var idType = provider switch
        {
            SupportedDatabase.Firebird => "BIGINT",
            _ => "BIGINT"
        };
        var stringType = provider switch
        {
            SupportedDatabase.Firebird => "VARCHAR(255)",
            _ => "VARCHAR(255)"
        };
        var versionType = "INT";

        var versionDefinition = provider switch
        {
            SupportedDatabase.Firebird => $"{versionColumn} {versionType} NOT NULL",
            _ => $"{versionColumn} {versionType} NOT NULL DEFAULT 1"
        };

        return $@"
CREATE TABLE {table} (
    {idColumn} {idType} PRIMARY KEY,
    {nameColumn} {stringType} NOT NULL,
    {versionDefinition}
)";
    }
}

[Table("versioned_upsert_entities")]
public class VersionedUpsertEntity
{
    [Id][Column("id", DbType.Int64)] public long Id { get; set; }

    [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

    [Version]
    [Column("version", DbType.Int32)]
    public int Version { get; set; }
}
