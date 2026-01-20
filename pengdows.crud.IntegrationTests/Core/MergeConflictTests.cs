using System;
using System.Data;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Integration tests that exercise merge/conflict handling via versioned updates and upserts.
/// </summary>
public class MergeConflictTests : DatabaseTestBase
{
    public MergeConflictTests(ITestOutputHelper output) : base(output) { }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        context.TypeMapRegistry.Register<VersionedEntity>();
        context.TypeMapRegistry.Register<MergeRecord>();
        return Task.CompletedTask;
    }

    [Fact]
    public Task VersionedEntity_ConcurrentUpdate_DetectsConflict()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await RecreateTableAsync(context, "versioned_entities", BuildVersionedEntityTableSql(provider, context));

            var helper = new EntityHelper<VersionedEntity, long>(context);
            var initial = new VersionedEntity
            {
                Id = 1,
                Name = "original"
            };

            await helper.CreateAsync(initial, context);

            await using var concurrentContext = await CreateAdditionalContextAsync(provider);
            concurrentContext.TypeMapRegistry.Register<VersionedEntity>();
            var concurrentHelper = new EntityHelper<VersionedEntity, long>(concurrentContext);

            var firstCopy = await helper.RetrieveOneAsync(initial.Id, context);
            var secondCopy = await concurrentHelper.RetrieveOneAsync(initial.Id, concurrentContext);

            firstCopy!.Name = "first";
            var firstUpdate = await helper.UpdateAsync(firstCopy, context);
            Assert.Equal(1, firstUpdate);

            secondCopy!.Name = "second";
            var secondUpdate = await concurrentHelper.UpdateAsync(secondCopy, concurrentContext);
            Assert.Equal(0, secondUpdate);

            var final = await helper.RetrieveOneAsync(initial.Id, context);
            Assert.NotNull(final);
            Assert.Equal("first", final!.Name);
            Output.WriteLine($"{provider}: final name {final.Name} at version {final.Version}");
        });
    }

    [Fact]
    public Task MergeRecord_UpsertAfterRemoteChange_ProducesCombinedValue()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await RecreateTableAsync(context, "merge_records", BuildMergeRecordTableSql(provider, context));

            var helper = new EntityHelper<MergeRecord, long>(context);
            var baseRecord = new MergeRecord
            {
                Id = 1,
                RecordKey = "merge-key",
                Value = 10,
                LastUpdated = DateTime.UtcNow
            };

            await helper.CreateAsync(baseRecord, context);

            await using var remoteContext = await CreateAdditionalContextAsync(provider);
            remoteContext.TypeMapRegistry.Register<MergeRecord>();
            var remoteHelper = new EntityHelper<MergeRecord, long>(remoteContext);
            var remoteCopy = await remoteHelper.RetrieveOneAsync(new MergeRecord { RecordKey = baseRecord.RecordKey }, remoteContext);
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
            Assert.Equal(1, merged);

            var final = await helper.RetrieveOneAsync(new MergeRecord { RecordKey = baseRecord.RecordKey }, context);
            Assert.Equal(25, final!.Value);
            Assert.True(final.LastUpdated >= mergeCandidate.LastUpdated, "LastUpdated should reflect the merge point");
            Output.WriteLine($"{provider}: merged value {final.Value} at {final.LastUpdated:o}");
        });
    }

    private static async Task RecreateTableAsync(IDatabaseContext context, string tableName, string createSql)
    {
        await DropTableIfExistsAsync(context, tableName);
        using var container = context.CreateSqlContainer(createSql);
        await container.ExecuteNonQueryAsync();
    }

    private static string BuildVersionedEntityTableSql(SupportedDatabase provider, IDatabaseContext context)
    {
        var table = context.WrapObjectName("versioned_entities");
        var idColumn = context.WrapObjectName("id");
        var nameColumn = context.WrapObjectName("name");
        var versionColumn = context.WrapObjectName("version");

        var idType = GetBigIntType(provider);
        var stringType = GetStringType(provider);
        var versionType = GetIntType(provider);

        return $@"
CREATE TABLE {table} (
    {idColumn} {idType} PRIMARY KEY,
    {nameColumn} {stringType} NOT NULL,
    {versionColumn} {versionType} NOT NULL DEFAULT 1
)";
    }

    private static string BuildMergeRecordTableSql(SupportedDatabase provider, IDatabaseContext context)
    {
        var table = context.WrapObjectName("merge_records");
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
    {idColumn} {idType} PRIMARY KEY,
    {keyColumn} {stringType} NOT NULL,
    {valueColumn} {intType} NOT NULL,
    {updatedColumn} {dateType} NOT NULL,
    UNIQUE ({keyColumn})
)";
    }

    private static string GetBigIntType(SupportedDatabase provider) =>
        provider switch
        {
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.Oracle => "NUMBER(19)",
            _ => "BIGINT"
        };

    private static string GetIntType(SupportedDatabase provider) =>
        provider switch
        {
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.Firebird => "INTEGER",
            _ => "INT"
        };

    private static string GetStringType(SupportedDatabase provider) =>
        provider switch
        {
            SupportedDatabase.Sqlite => "TEXT",
            SupportedDatabase.SqlServer => "NVARCHAR(255)",
            SupportedDatabase.Oracle => "VARCHAR2(255)",
            SupportedDatabase.Firebird => "VARCHAR(255)",
            _ => "VARCHAR(255)"
        };

    private static string GetDateTimeType(SupportedDatabase provider) =>
        provider switch
        {
            SupportedDatabase.Sqlite => "TEXT",
            SupportedDatabase.SqlServer => "DATETIME2",
            SupportedDatabase.MySql => "DATETIME",
            SupportedDatabase.MariaDb => "DATETIME",
            _ => "TIMESTAMP"
        };
}

[Table("versioned_entities")]
public class VersionedEntity
{
    [Id]
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [Column("name", DbType.String)]
    public string Name { get; set; } = string.Empty;

    [Version]
    [Column("version", DbType.Int32)]
    public int Version { get; set; }
}

[Table("merge_records")]
public class MergeRecord
{
    [Id]
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [PrimaryKey(1)]
    [Column("record_key", DbType.String)]
    public string RecordKey { get; set; } = string.Empty;

    [Column("value", DbType.Int32)]
    public int Value { get; set; }

    [Column("last_updated", DbType.DateTime)]
    public DateTime LastUpdated { get; set; }
}
