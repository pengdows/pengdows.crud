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

            await using var concurrentContext = await CreateAdditionalContextAsync(provider);
            concurrentContext.RegisterEntity<VersionedEntity>();
            var concurrentHelper = new TableGateway<VersionedEntity, long>(concurrentContext);

            var firstCopy = await helper.RetrieveOneAsync(initial.Id, context);
            var secondCopy = await concurrentHelper.RetrieveOneAsync(initial.Id, concurrentContext);

            firstCopy!.Name = "first";
            var firstUpdate = await helper.UpdateAsync(firstCopy, context);
            Assert.Equal(1, firstUpdate);

            secondCopy!.Name = "second";
            await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
                await concurrentHelper.UpdateAsync(secondCopy, concurrentContext));

            var final = await helper.RetrieveOneAsync(initial.Id, context);
            Assert.NotNull(final);
            Assert.Equal("first", final!.Name);
            Output.WriteLine($"{provider}: final name {final.Name} at version {final.Version}");
        });
    }

    [SkippableFact]
    public Task BatchUpdate_VersionedEntities_IncrementsVersionAndDetectsPartialStaleConflict()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await RecreateTableAsync(context, "versioned_entities", BuildVersionedEntityTableSql(provider, context));

            var helper = new TableGateway<VersionedEntity, long>(context);
            var entities = new List<VersionedEntity>
            {
                new() { Id = 1, Name = "a", Version = 1 },
                new() { Id = 2, Name = "b", Version = 1 },
                new() { Id = 3, Name = "c", Version = 1 }
            };

            foreach (var entity in entities)
            {
                await helper.CreateAsync(entity, context);
            }

            // Happy path: all three rows fresh — a real multi-row batch UPDATE (dialects with
            // SupportsBatchUpdate use MERGE/UPDATE-FROM-VALUES; others fall back to per-entity,
            // but the caller-visible contract — affected count, version increment — must match).
            foreach (var entity in entities)
            {
                entity.Name += "-updated";
            }

            var affected = await helper.UpdateAsync(entities, context);
            Assert.Equal(3, affected);

            foreach (var entity in entities)
            {
                var reread = await helper.RetrieveOneAsync(entity.Id, context);
                Assert.NotNull(reread);
                Assert.Equal(2, reread!.Version);
                Assert.EndsWith("-updated", reread.Name);
            }

            Output.WriteLine($"{provider}: batch update of 3 fresh rows incremented all versions to 2");

            // Conflict path: entities[0] is intentionally stale (still holds Version=2's
            // in-memory copy from before this round even started — simulate by re-fetching
            // fresh copies for [1] and [2] but reusing the ALREADY-INCREMENTED-BY-THIS-TEST
            // entities[0] instance whose Version the DB has since moved past via a concurrent
            // write from a second context).
            await using var concurrentContext = await CreateAdditionalContextAsync(provider);
            concurrentContext.RegisterEntity<VersionedEntity>();
            var concurrentHelper = new TableGateway<VersionedEntity, long>(concurrentContext);
            var staleTarget = await concurrentHelper.RetrieveOneAsync(entities[0].Id, concurrentContext);
            staleTarget!.Name = "raced-ahead";
            var raceUpdate = await concurrentHelper.UpdateAsync(staleTarget, concurrentContext);
            Assert.Equal(1, raceUpdate);

            var fresh1 = await helper.RetrieveOneAsync(entities[1].Id, context);
            var fresh2 = await helper.RetrieveOneAsync(entities[2].Id, context);
            fresh1!.Name = "second-round-b";
            fresh2!.Name = "second-round-c";
            entities[0].Name = "second-round-a"; // still holds the now-stale Version=2

            var conflictBatch = new List<VersionedEntity> { entities[0], fresh1, fresh2 };
            await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
                await helper.UpdateAsync(conflictBatch, context));

            var staleReread = await helper.RetrieveOneAsync(entities[0].Id, context);
            var freshReread1 = await helper.RetrieveOneAsync(entities[1].Id, context);
            var freshReread2 = await helper.RetrieveOneAsync(entities[2].Id, context);
            Assert.Equal("raced-ahead", staleReread!.Name); // untouched by the stale-version attempt

            if (context.Dialect.SupportsBatchUpdate)
            {
                // A real multi-row MERGE/UPDATE-FROM-VALUES statement applies each source row
                // independently — only the stale row fails to match, so the two fresh rows in the
                // SAME batch SQL statement must still have been written. Re-read from the database
                // rather than trusting in-memory state, per the batch-conflict message's own
                // guidance.
                Assert.Equal("second-round-b", freshReread1!.Name);
                Assert.Equal("second-round-c", freshReread2!.Name);
                Assert.Equal(3, freshReread1.Version);
                Assert.Equal(3, freshReread2.Version);
                Output.WriteLine($"{provider}: batch update with one stale row threw and left the other two committed");
            }
            else
            {
                // Dialects without SupportsBatchUpdate fall back to one UPDATE statement per
                // entity, executed sequentially in list order — the stale entity was placed first,
                // so the loop throws immediately and the two fresh rows after it are never even
                // attempted (unlike the atomic multi-row path above), leaving them at their
                // first-round values.
                Assert.Equal("b-updated", freshReread1!.Name);
                Assert.Equal("c-updated", freshReread2!.Name);
                Assert.Equal(2, freshReread1.Version);
                Assert.Equal(2, freshReread2!.Version);
                Output.WriteLine($"{provider}: per-entity fallback threw on the first (stale) entity and never attempted the rest");
            }
        });
    }

    [SkippableFact]
    public Task MergeRecord_UpsertAfterRemoteChange_ProducesCombinedValue()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await RecreateTableAsync(context, "merge_records", BuildMergeRecordTableSql(provider, context));

            var helper = new TableGateway<MergeRecord, long>(context);
            var baseRecord = new MergeRecord
            {
                Id = 1,
                RecordKey = "merge-key",
                Value = 10,
                LastUpdated = DateTime.UtcNow
            };

            await helper.CreateAsync(baseRecord, context);

            await using var remoteContext = await CreateAdditionalContextAsync(provider);
            remoteContext.RegisterEntity<MergeRecord>();
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

    private static async Task RecreateTableAsync(IDatabaseContext context, string tableName, string createSql)
    {
        await DropTableIfExistsAsync(context, tableName);
        await using var container = context.CreateSqlContainer(createSql);
        await container.ExecuteNonQueryAsync();
    }

    private static string BuildVersionedEntityTableSql(SupportedDatabase provider, IDatabaseContext context)
    {
        var table = IntegrationObjectNameHelper.Table(context, "versioned_entities");
        var idColumn = context.WrapObjectName("id");
        var nameColumn = context.WrapObjectName("name");
        var versionColumn = context.WrapObjectName("version");

        var idType = GetBigIntType(provider);
        var stringType = GetStringType(provider);
        var versionType = GetIntType(provider);

        var versionDefinition = provider switch
        {
            SupportedDatabase.Firebird => $"{versionColumn} {versionType} NOT NULL",
            SupportedDatabase.Oracle => $"{versionColumn} {versionType} DEFAULT 1 NOT NULL",
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

        var idType = GetBigIntType(provider);
        var stringType = GetStringType(provider);
        var intType = GetIntType(provider);
        var dateType = GetDateTimeType(provider);

        return $@"
CREATE TABLE {table} (
    {idColumn} {idType} NOT NULL PRIMARY KEY,
    {keyColumn} {stringType} NOT NULL,
    {valueColumn} {intType} NOT NULL,
    {updatedColumn} {dateType} NOT NULL,
    UNIQUE ({keyColumn})
)";
    }

    private static string GetBigIntType(SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.Oracle => "NUMBER(19)",
            SupportedDatabase.Firebird => "BIGINT",
            _ => "BIGINT"
        };
    }

    private static string GetIntType(SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.Firebird => "INTEGER",
            _ => "INT"
        };
    }

    private static string GetStringType(SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Sqlite => "TEXT",
            SupportedDatabase.SqlServer => "NVARCHAR(255)",
            SupportedDatabase.Oracle => "VARCHAR2(255)",
            SupportedDatabase.Firebird => "VARCHAR(255)",
            _ => "VARCHAR(255)"
        };
    }

    private static string GetDateTimeType(SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Sqlite => "TEXT",
            SupportedDatabase.SqlServer => "DATETIME2",
            SupportedDatabase.MySql => "DATETIME",
            SupportedDatabase.MariaDb => "DATETIME",
            _ => "TIMESTAMP"
        };
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
