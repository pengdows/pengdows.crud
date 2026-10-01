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
/// Integration tests that exercise merge/conflict handling via versioned updates and upserts.
/// </summary>
[Collection("IntegrationTests")]
public class MergeConflictTests : DatabaseTestBase
{
    public MergeConflictTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        context.RegisterEntity<VersionedEntity>();
        context.RegisterEntity<MergeRecord>();
        return Task.CompletedTask;
    }

    [SkippableFact]
    public Task VersionedEntity_ConcurrentUpdate_DetectsConflict()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await RecreateTableAsync(context, "versioned_entities", BuildVersionedEntityTableSql(provider, context));

            var helper = new TableGateway<VersionedEntity, long>(context);
            var initial = new VersionedEntity
            {
                Id = 1,
                Name = "original",
                Version = 1
            };

            await helper.CreateAsync(initial, context);

            // A second reader/writer of the same row. The conflict is detected by the version column in
            // SQL, so the same context serves; a second context on the same database is unsupported
            // (one DatabaseContext per connection string, especially for file-based databases).
            var concurrentContext = context;
            var concurrentHelper = new TableGateway<VersionedEntity, long>(concurrentContext);

            var firstCopy = await helper.RetrieveOneAsync(initial.Id, context);
            var secondCopy = await concurrentHelper.RetrieveOneAsync(initial.Id, concurrentContext);

            firstCopy!.Name = "first";
            var firstUpdate = await helper.UpdateAsync(firstCopy, context);
            Assert.Equal(1, firstUpdate);

            secondCopy!.Name = "second";
            var conflictException = await Record.ExceptionAsync(async () =>
                await concurrentHelper.UpdateAsync(secondCopy, concurrentContext));

            // Normally this manifests as ConcurrencyConflictException (the second UPDATE
            // completes but its WHERE ... AND version = @stale matches 0 rows, because the first
            // UPDATE already committed a new version by the time this one runs). But
            // FirebirdExceptionTranslator documents that Firebird's SQLSTATE 40001 cannot
            // distinguish a true lock-cycle deadlock from an optimistic update conflict — both
            // produce the identical signature — so a SerializationConflictException is an equally
            // valid, deliberately-classified outcome here specifically for Firebird, depending on
            // exactly how much the two writes' timing overlaps.
            if (provider == SupportedDatabase.Firebird)
            {
                Assert.True(
                    conflictException is ConcurrencyConflictException or SerializationConflictException,
                    $"Expected ConcurrencyConflictException or SerializationConflictException, got {conflictException?.GetType().Name}: {conflictException?.Message}");
            }
            else
            {
                Assert.IsType<ConcurrencyConflictException>(conflictException);
            }

            var final = await helper.RetrieveOneAsync(initial.Id, context);
            Assert.NotNull(final);
            Assert.Equal("first", final!.Name);
            Output.WriteLine($"{provider}: final name {final.Name} at version {final.Version}");
        });
    }

    // A stale-version upsert must not silently overwrite a newer row. The dialects whose upsert
    // syntax has no conditional update (MySQL-family ON DUPLICATE KEY, Firebird UPDATE OR INSERT)
    // are the documented exceptions; every other provider must detect the conflict.
    [SkippableFact]
    public Task VersionedEntity_StaleUpsert_DetectsConflict()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await RecreateTableAsync(context, "versioned_entities", BuildVersionedEntityTableSql(provider, context));

            var helper = new TableGateway<VersionedEntity, long>(context);
            await helper.CreateAsync(new VersionedEntity { Id = 1, Name = "original", Version = 1 }, context);

            var firstCopy = await helper.RetrieveOneAsync(1, context);
            var staleCopy = await helper.RetrieveOneAsync(1, context);

            firstCopy!.Name = "first";
            Assert.Equal(1, await helper.UpdateAsync(firstCopy, context));

            staleCopy!.Name = "stale";
            if (UpsertRefusesVersionedEntities(context))
            {
                await Assert.ThrowsAsync<NotSupportedException>(async () => await helper.UpsertAsync(staleCopy, context));
                Output.WriteLine($"{provider}: capability - versioned upsert refused (no conditional MERGE matched clause)");
                return;
            }

            if (!UpsertDetectsStaleVersion(context))
            {
                Output.WriteLine(
                    $"{provider}: capability - upsert rows affected cannot reveal a skipped stale version; not asserted");
                return;
            }

            await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
                await helper.UpsertAsync(staleCopy, context));

            var final = await helper.RetrieveOneAsync(1, context);
            Assert.Equal("first", final!.Name);
            Assert.Equal(2, final.Version);
        });
    }

    // A batch update that contains a stale [Version] row must throw and leave that row alone,
    // on every provider (multi-row UPDATE dialects included).
    [SkippableFact]
    public Task VersionedEntity_BatchUpdateWithStaleRow_DetectsConflict()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await RecreateTableAsync(context, "versioned_entities", BuildVersionedEntityTableSql(provider, context));

            var helper = new TableGateway<VersionedEntity, long>(context);
            await helper.CreateAsync(new VersionedEntity { Id = 1, Name = "one", Version = 1 }, context);
            await helper.CreateAsync(new VersionedEntity { Id = 2, Name = "two", Version = 1 }, context);

            var rowOne = await helper.RetrieveOneAsync(1, context);
            var staleTwo = await helper.RetrieveOneAsync(2, context);

            var freshTwo = await helper.RetrieveOneAsync(2, context);
            freshTwo!.Name = "two-updated";
            Assert.Equal(1, await helper.UpdateAsync(freshTwo, context));

            rowOne!.Name = "one-batch";
            staleTwo!.Name = "two-stale";
            await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
                await helper.BatchUpdateAsync(new List<VersionedEntity> { rowOne, staleTwo }, context));

            var finalTwo = await helper.RetrieveOneAsync(2, context);
            Assert.Equal("two-updated", finalTwo!.Name);
            Assert.Equal(2, finalTwo.Version);
        });
    }

    [SkippableFact]
    public Task MergeRecord_UpsertAfterRemoteChange_ProducesCombinedValue()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Capability: MergeRecord upserts on its record_key business key, a secondary unique
            // index; SupportsOnConflictOnSecondaryUniqueKey = false (Spanner) can't target it.
            if (!pengdows.crud.dialects.InternalSqlDialectExtensions.SupportsOnConflictOnSecondaryUniqueKey(
                    context.GetDialect()))
            {
                Output.WriteLine($"Skipping for {provider}: dialect SupportsOnConflictOnSecondaryUniqueKey is false");
                return;
            }

            await RecreateTableAsync(context, "merge_records", BuildMergeRecordTableSql(provider, context),
                IntegrationObjectNameHelper.SpannerUniqueIndexSql(context,
                    IntegrationObjectNameHelper.Table(context, "merge_records"), "ux_merge_records_record_key",
                    context.WrapObjectName("record_key")));

            var helper = new TableGateway<MergeRecord, long>(context);
            var baseRecord = new MergeRecord
            {
                Id = 1,
                RecordKey = "merge-key",
                Value = 10,
                LastUpdated = DateTime.UtcNow
            };

            await helper.CreateAsync(baseRecord, context);

            // Another writer of the same row, through the same context (see above).
            var remoteContext = context;
            var remoteHelper = new TableGateway<MergeRecord, long>(remoteContext);
            var remoteCopy =
                await remoteHelper.RetrieveOneAsync(new MergeRecord { RecordKey = baseRecord.RecordKey },
                    remoteContext);
            remoteCopy!.Value = 20;
            remoteCopy.LastUpdated = DateTime.UtcNow;
            await remoteHelper.UpdateAsync(remoteCopy, remoteContext);

            var current = await helper.RetrieveOneAsync(new MergeRecord { RecordKey = baseRecord.RecordKey }, context);
            var mergeCandidate = new MergeRecord
            {
                Id = current!.Id,
                RecordKey = current.RecordKey,
                Value = current.Value + 5,
                LastUpdated = DateTime.UtcNow
            };

            var merged = await helper.UpsertAsync(mergeCandidate, context);
            Assert.True(merged is 1 or 2, $"Expected 1 or 2 affected rows, got {merged}");

            var final = await helper.RetrieveOneAsync(new MergeRecord { RecordKey = baseRecord.RecordKey }, context);
            Assert.Equal(25, final!.Value);
            var tolerance = TimeSpan.FromSeconds(1);
            Assert.True(final.LastUpdated + tolerance >= mergeCandidate.LastUpdated,
                "LastUpdated should reflect the merge point");
            Output.WriteLine($"{provider}: merged value {final.Value} at {final.LastUpdated:o}");
        });
    }

    private static async Task RecreateTableAsync(IDatabaseContext context, string tableName, string createSql,
        string? extraSql = null)
    {
        await DropTableIfExistsAsync(context, tableName);

        // extraSql is Spanner's CREATE UNIQUE INDEX. Sent in the same batch as CREATE TABLE, Spanner
        // applies both as one schema change on an empty table (~9s); a separate CREATE INDEX runs its
        // own backfill schema change (~90s on Spanner Omni, measured).
        var sql = extraSql is null ? createSql : createSql + ";\n" + extraSql;
        await using var container = context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    private static string BuildVersionedEntityTableSql(SupportedDatabase provider, IDatabaseContext context)
    {
        var table = IntegrationObjectNameHelper.Table(context, "versioned_entities");
        var idColumn = context.WrapObjectName("id");
        var nameColumn = context.WrapObjectName("name");
        var versionColumn = context.WrapObjectName("version");

        var idType = IntegrationObjectNameHelper.BigIntType(provider);
        var stringType = IntegrationObjectNameHelper.StringType(provider);
        var versionType = IntegrationObjectNameHelper.IntType(provider);

        var versionDefinition = provider switch
        {
            SupportedDatabase.Firebird => $"{versionColumn} {versionType} NOT NULL",
            SupportedDatabase.Oracle or SupportedDatabase.Informix or SupportedDatabase.SybaseASE
                => $"{versionColumn} {versionType} DEFAULT 1 NOT NULL",
            _ => $"{versionColumn} {versionType} NOT NULL DEFAULT 1"
        };

        return $@"
CREATE TABLE {table} (
    {idColumn} {idType} NOT NULL PRIMARY KEY,
    {nameColumn} {stringType} NOT NULL,
    {versionDefinition}
)";
    }

    private static string BuildMergeRecordTableSql(SupportedDatabase provider, IDatabaseContext context)
    {
        var table = IntegrationObjectNameHelper.Table(context, "merge_records");
        var idColumn = context.WrapObjectName("id");
        var keyColumn = context.WrapObjectName("record_key");
        var valueColumn = context.WrapObjectName("value");
        var updatedColumn = context.WrapObjectName("last_updated");

        var idType = IntegrationObjectNameHelper.BigIntType(provider);
        var stringType = IntegrationObjectNameHelper.StringType(provider);
        var intType = IntegrationObjectNameHelper.IntType(provider);
        var dateType = IntegrationObjectNameHelper.DateTimeType(provider);

        var uniqueClause = IntegrationObjectNameHelper.InlineUniqueConstraintClause(provider, keyColumn);

        return $@"
CREATE TABLE {table} (
    {idColumn} {idType} NOT NULL PRIMARY KEY,
    {keyColumn} {stringType} NOT NULL,
    {valueColumn} {intType} NOT NULL,
    {updatedColumn} {dateType} NOT NULL{IntegrationObjectNameHelper.InlineUniqueConstraintClause(context, keyColumn)}
)";
    }

}

[Table("versioned_entities")]
public class VersionedEntity
{
    [Id][Column("id", DbType.Int64)] public long Id { get; set; }

    [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

    [Version]
    [Column("version", DbType.Int32)]
    public int Version { get; set; }
}

[Table("merge_records")]
public class MergeRecord
{
    [Id][Column("id", DbType.Int64)] public long Id { get; set; }

    [PrimaryKey(1)]
    [Column("record_key", DbType.String)]
    public string RecordKey { get; set; } = string.Empty;

    [Column("value", DbType.Int32)] public int Value { get; set; }

    [Column("last_updated", DbType.DateTime)]
    public DateTime LastUpdated { get; set; }
}
