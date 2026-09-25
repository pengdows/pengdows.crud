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
/// Live verification of [Version] optimistic concurrency against real engines (ported from 3.0 and
/// extended for 2.0.6's BP-201/202/203):
/// <list type="bullet">
/// <item>UpsertAsync / BatchUpsertAsync reject a stale version wherever the generated SQL carries a
/// version guard (MERGE, ON CONFLICT ... DO UPDATE ... WHERE), and cannot detect it on MySQL-family
/// ON DUPLICATE KEY UPDATE or Firebird UPDATE OR INSERT (documented limitation).</item>
/// <item>PrimaryKeyTableGateway UpdateAsync / BatchUpdateAsync throw on a stale version (BP-201).</item>
/// <item>A successful UpdateAsync / BatchUpdateAsync writes the incremented version back into the
/// entity, so the same instance can be updated again (BP-203).</item>
/// </list>
/// </summary>
[Collection("IntegrationTests")]
public class VersionedUpsertConflictTests : DatabaseTestBase
{
    private const string TableName = "versioned_upsert_entities";
    private const string PkTableName = "versioned_pk_upsert_entities";

    public VersionedUpsertConflictTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        context.RegisterEntity<VersionedUpsertEntity>();
        context.RegisterEntity<VersionedPkUpsertEntity>();
        await RecreateTablesAsync(provider, context);
    }

    // MySQL-family ON DUPLICATE KEY UPDATE and Firebird UPDATE OR INSERT have no conditional update,
    // so a stale version on an existing row can't be detected by any upsert there.
    private static bool UpsertCannotDetectStaleVersion(SupportedDatabase provider) =>
        provider is SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb
            or SupportedDatabase.AuroraMySql or SupportedDatabase.SingleStore or SupportedDatabase.Firebird;

    [SkippableFact]
    public Task UpsertAsync_StaleVersion_ThrowsWhereDetectable()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await RecreateTablesAsync(provider, context);
            var helper = new TableGateway<VersionedUpsertEntity, long>(context);
            await helper.CreateAsync(new VersionedUpsertEntity { Id = 1, Name = "original" }, context);

            var holderA = await helper.RetrieveOneAsync(1, context);
            var holderB = await helper.RetrieveOneAsync(1, context);

            holderA!.Name = "updated-by-a";
            Assert.Equal(1, await helper.UpdateAsync(holderA, context));

            holderB!.Name = "updated-by-b";
            if (UpsertCannotDetectStaleVersion(provider))
            {
                var ex = await Record.ExceptionAsync(async () => await helper.UpsertAsync(holderB, context));
                Assert.Null(ex);
                Output.WriteLine($"{provider}: stale upsert not detectable (documented limitation)");
                return;
            }

            await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
                await helper.UpsertAsync(holderB, context));

            var final = await helper.RetrieveOneAsync(1, context);
            Assert.Equal("updated-by-a", final!.Name);
            Assert.Equal(2, final.Version);
        });
    }

    [SkippableFact]
    public Task BatchUpsertAsync_StaleVersion_ThrowsWhereDetectable()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await RecreateTablesAsync(provider, context);
            var helper = new TableGateway<VersionedUpsertEntity, long>(context);
            await helper.CreateAsync(new VersionedUpsertEntity { Id = 1, Name = "one" }, context);
            await helper.CreateAsync(new VersionedUpsertEntity { Id = 2, Name = "two" }, context);

            var staleTwo = await helper.RetrieveOneAsync(2, context);
            var freshTwo = await helper.RetrieveOneAsync(2, context);
            freshTwo!.Name = "two-updated";
            Assert.Equal(1, await helper.UpdateAsync(freshTwo, context));

            var one = await helper.RetrieveOneAsync(1, context);
            one!.Name = "one-batch";
            staleTwo!.Name = "two-stale";
            var batch = new List<VersionedUpsertEntity> { one, staleTwo };

            if (UpsertCannotDetectStaleVersion(provider))
            {
                var ex = await Record.ExceptionAsync(async () => await helper.BatchUpsertAsync(batch, context));
                Assert.Null(ex);
                Output.WriteLine($"{provider}: stale batch upsert not detectable (documented limitation)");
                return;
            }

            await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
                await helper.BatchUpsertAsync(batch, context));

            var finalTwo = await helper.RetrieveOneAsync(2, context);
            Assert.Equal("two-updated", finalTwo!.Name);
            Assert.Equal(2, finalTwo.Version);
            Output.WriteLine($"{provider}: stale batch upsert rejected, newer row kept");
        });
    }

    [SkippableFact]
    public Task UpdateAsync_ReusedInstance_WritesBackVersionAndSucceedsAgain()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await RecreateTablesAsync(provider, context);
            var helper = new TableGateway<VersionedUpsertEntity, long>(context);
            var entity = new VersionedUpsertEntity { Id = 1, Name = "original" };
            await helper.CreateAsync(entity, context);
            Assert.Equal(1, entity.Version);

            entity.Name = "first";
            Assert.Equal(1, await helper.UpdateAsync(entity, context));
            Assert.Equal(2, entity.Version);

            entity.Name = "second";
            Assert.Equal(1, await helper.UpdateAsync(entity, context));
            Assert.Equal(3, entity.Version);

            var final = await helper.RetrieveOneAsync(1, context);
            Assert.Equal("second", final!.Name);
            Assert.Equal(3, final.Version);
        });
    }

    [SkippableFact]
    public Task BatchUpdateAsync_ReusedInstances_WritesBackVersionAndSucceedsAgain()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await RecreateTablesAsync(provider, context);
            var helper = new TableGateway<VersionedUpsertEntity, long>(context);
            var entities = new List<VersionedUpsertEntity>
            {
                new() { Id = 1, Name = "a" },
                new() { Id = 2, Name = "b" }
            };
            foreach (var e in entities)
            {
                await helper.CreateAsync(e, context);
            }

            for (var round = 2; round <= 3; round++)
            {
                foreach (var e in entities)
                {
                    e.Name = $"{e.Name}-r{round}";
                }

                Assert.Equal(2, await helper.BatchUpdateAsync(entities, context));
                Assert.All(entities, e => Assert.Equal(round, e.Version));
            }

            foreach (var e in entities)
            {
                var reread = await helper.RetrieveOneAsync(e.Id, context);
                Assert.Equal(3, reread!.Version);
                Assert.Equal(e.Name, reread.Name);
            }
        });
    }

    [SkippableFact]
    public Task PrimaryKeyGateway_UpdateAndBatchUpdate_StaleVersion_Throws()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await RecreateTablesAsync(provider, context);
            var gateway = new PrimaryKeyTableGateway<VersionedPkUpsertEntity>(context);
            await gateway.CreateAsync(new VersionedPkUpsertEntity { Code = "a", Name = "a" }, context);
            await gateway.CreateAsync(new VersionedPkUpsertEntity { Code = "b", Name = "b" }, context);

            var staleB = await gateway.RetrieveOneAsync(new VersionedPkUpsertEntity { Code = "b" }, context);
            var freshB = await gateway.RetrieveOneAsync(new VersionedPkUpsertEntity { Code = "b" }, context);
            freshB!.Name = "b-fresh";
            Assert.Equal(1, await gateway.UpdateAsync(freshB, context));

            staleB!.Name = "b-stale";
            await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
                await gateway.UpdateAsync(staleB, context));

            var a = await gateway.RetrieveOneAsync(new VersionedPkUpsertEntity { Code = "a" }, context);
            a!.Name = "a-batch";
            var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
                await gateway.BatchUpdateAsync(new List<VersionedPkUpsertEntity> { a, staleB }, context));
            Assert.Contains("code=b", ex.Message);

            var finalB = await gateway.RetrieveOneAsync(new VersionedPkUpsertEntity { Code = "b" }, context);
            Assert.Equal("b-fresh", finalB!.Name);
            Assert.Equal(2, finalB.Version);
        });
    }

    [SkippableFact]
    public Task PrimaryKeyGateway_ReusedInstance_WritesBackVersionAndSucceedsAgain()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await RecreateTablesAsync(provider, context);
            var gateway = new PrimaryKeyTableGateway<VersionedPkUpsertEntity>(context);
            var single = new VersionedPkUpsertEntity { Code = "s", Name = "s" };
            var batch = new List<VersionedPkUpsertEntity>
            {
                new() { Code = "x", Name = "x" },
                new() { Code = "y", Name = "y" }
            };
            await gateway.CreateAsync(single, context);
            foreach (var e in batch)
            {
                await gateway.CreateAsync(e, context);
            }

            for (var round = 2; round <= 3; round++)
            {
                single.Name = $"s-r{round}";
                Assert.Equal(1, await gateway.UpdateAsync(single, context));
                Assert.Equal(round, single.Version);

                foreach (var e in batch)
                {
                    e.Name = $"{e.Code}-r{round}";
                }

                Assert.Equal(2, await gateway.BatchUpdateAsync(batch, context));
                Assert.All(batch, e => Assert.Equal(round, e.Version));
            }

            var reread = await gateway.RetrieveOneAsync(new VersionedPkUpsertEntity { Code = "y" }, context);
            Assert.Equal("y-r3", reread!.Name);
            Assert.Equal(3, reread.Version);
        });
    }

    [SkippableFact]
    public Task PrimaryKeyGateway_BatchUpsertAsync_StaleVersion_ThrowsWhereDetectable()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await RecreateTablesAsync(provider, context);
            var gateway = new PrimaryKeyTableGateway<VersionedPkUpsertEntity>(context);
            await gateway.CreateAsync(new VersionedPkUpsertEntity { Code = "a", Name = "a" }, context);
            await gateway.CreateAsync(new VersionedPkUpsertEntity { Code = "b", Name = "b" }, context);

            var staleB = await gateway.RetrieveOneAsync(new VersionedPkUpsertEntity { Code = "b" }, context);
            var freshB = await gateway.RetrieveOneAsync(new VersionedPkUpsertEntity { Code = "b" }, context);
            freshB!.Name = "b-fresh";
            Assert.Equal(1, await gateway.UpdateAsync(freshB, context));

            var a = await gateway.RetrieveOneAsync(new VersionedPkUpsertEntity { Code = "a" }, context);
            a!.Name = "a-upsert";
            staleB!.Name = "b-stale";
            var batch = new List<VersionedPkUpsertEntity> { a, staleB };

            if (UpsertCannotDetectStaleVersion(provider))
            {
                var ex = await Record.ExceptionAsync(async () => await gateway.BatchUpsertAsync(batch, context));
                Assert.Null(ex);
                Output.WriteLine($"{provider}: stale PK batch upsert not detectable (documented limitation)");
                return;
            }

            await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
                await gateway.BatchUpsertAsync(batch, context));

            var finalB = await gateway.RetrieveOneAsync(new VersionedPkUpsertEntity { Code = "b" }, context);
            Assert.Equal("b-fresh", finalB!.Name);
            Assert.Equal(2, finalB.Version);
        });
    }

    private static async Task RecreateTablesAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await DropTableIfExistsAsync(context, TableName);
        await DropTableIfExistsAsync(context, PkTableName);
        await using (var sc = context.CreateSqlContainer(BuildTableSql(provider, context, TableName, "id",
                         GetBigIntType(provider))))
        {
            await sc.ExecuteNonQueryAsync();
        }

        await using (var sc = context.CreateSqlContainer(BuildTableSql(provider, context, PkTableName, "code",
                         GetStringType(provider, 64))))
        {
            await sc.ExecuteNonQueryAsync();
        }
    }

    private static string BuildTableSql(SupportedDatabase provider, IDatabaseContext context, string tableName,
        string keyName, string keyType)
    {
        var table = IntegrationObjectNameHelper.Table(context, tableName);
        var keyColumn = context.WrapObjectName(keyName);
        var nameColumn = context.WrapObjectName("name");
        var versionColumn = context.WrapObjectName("version");
        var versionType = provider is SupportedDatabase.Sqlite or SupportedDatabase.Firebird ? "INTEGER" : "INT";

        var versionDefinition = provider switch
        {
            SupportedDatabase.Firebird => $"{versionColumn} {versionType} NOT NULL",
            SupportedDatabase.Oracle => $"{versionColumn} {versionType} DEFAULT 1 NOT NULL",
            _ => $"{versionColumn} {versionType} NOT NULL DEFAULT 1"
        };

        return $@"
CREATE TABLE {table} (
    {keyColumn} {keyType} NOT NULL PRIMARY KEY,
    {nameColumn} {GetStringType(provider, 255)} NOT NULL,
    {versionDefinition}
)";
    }

    private static string GetBigIntType(SupportedDatabase provider) => provider switch
    {
        SupportedDatabase.Sqlite => "INTEGER",
        SupportedDatabase.Oracle => "NUMBER(19)",
        _ => "BIGINT"
    };

    private static string GetStringType(SupportedDatabase provider, int length) => provider switch
    {
        SupportedDatabase.Sqlite => "TEXT",
        SupportedDatabase.SqlServer => $"NVARCHAR({length})",
        SupportedDatabase.Oracle => $"VARCHAR2({length})",
        _ => $"VARCHAR({length})"
    };
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

[Table("versioned_pk_upsert_entities")]
public class VersionedPkUpsertEntity
{
    [PrimaryKey][Column("code", DbType.String)] public string Code { get; set; } = string.Empty;

    [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

    [Version]
    [Column("version", DbType.Int32)]
    public int Version { get; set; }
}
