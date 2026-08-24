#region

using System.Data;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TableGatewayUpsertTests
{
    [Fact]
    public async Task BuildUpsert_OnConflict_UsesPrimaryKeyAndVersion()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=PostgreSql",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        await using var context = new DatabaseContext(cfg, factory);
        var helper = new TableGateway<ConflictEntity, long>(context,
            logger: NullLogger<TableGateway<ConflictEntity, long>>.Instance);

        var entity = new ConflictEntity
        {
            Id = 13,
            ExternalKey = "KEY",
            Value = "v1"
        };

        using var container = helper.BuildUpsert(entity, context);
        var sql = container.Query.ToString();

        // fakeDb's emulated PostgreSQL version (15.0) satisfies SupportsMerge, so this hits the
        // MERGE branch. MERGE's version-increment RHS must stay qualified with the target alias
        // "t." (always declared via "MERGE INTO ... t") even though PostgreSQL's
        // MergeUpdateRequiresTargetAlias is false for the LHS — an unqualified RHS reference is
        // ambiguous between the target and MERGE source on a real PostgreSQL server. See
        // BuildUpsertSqlGenerationTests.BuildUpsert_Merge_BumpsVersion_ForDialectWithoutTargetAlias_QualifiesCurrentValueWithTargetAlias.
        Assert.True(sql.Contains("ON CONFLICT") || sql.Contains("MERGE INTO"),
            "Expected Postgres upsert to use ON CONFLICT or MERGE.");
        if (sql.Contains("MERGE INTO"))
        {
            Assert.Contains("\"version\" = t.\"version\" + 1", sql);
        }
        else
        {
            Assert.Contains("\"version\" = \"version\" + 1", sql);
        }

        Assert.Equal(1, entity.Version);
    }

    [Fact]
    public void BuildUpsert_WithNonWritableIdAndNoPrimaryKey_ThrowsNotSupported()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite));
        var helper = new TableGateway<IdOnlyEntity, long>(context);

        var entity = new IdOnlyEntity
        {
            Value = "v1"
        };

        Assert.Throws<NotSupportedException>(() => helper.BuildUpsert(entity, context));
    }

    [Fact]
    public async Task BuildUpsert_PostgreSql_WithWritableId_OverridesSystemIdentityValue()
    {
        var configuration = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=PostgreSql",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(
            configuration,
            new fakeDbFactory(SupportedDatabase.PostgreSql));
        var gateway = new TableGateway<ExplicitIdentityEntity, int>(context);

        using var container = gateway.BuildUpsert(
            new ExplicitIdentityEntity { Id = 42, Value = "explicit identity" },
            context);

        Assert.Contains("OVERRIDING SYSTEM VALUE", container.Query.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildUpsert_CockroachDb_WithWritableId_DoesNotUsePostgreSqlOnlyIdentitySyntax()
    {
        var configuration = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=CockroachDb",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(
            configuration,
            new fakeDbFactory(SupportedDatabase.CockroachDb));
        var gateway = new TableGateway<ExplicitIdentityEntity, int>(context);

        using var container = gateway.BuildUpsert(
            new ExplicitIdentityEntity { Id = 42, Value = "explicit identity" },
            context);

        Assert.DoesNotContain("OVERRIDING SYSTEM VALUE", container.Query.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildUpsert_YugabyteDb_WithWritableId_OverridesSystemIdentityValue()
    {
        // Unlike CockroachDB, YugabyteDB's YSQL layer genuinely supports OVERRIDING SYSTEM VALUE
        // (confirmed against YugabyteDB's own docs) — it should behave like PostgreSQL here, not
        // like CockroachDB.
        var configuration = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=YugabyteDb",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(
            configuration,
            new fakeDbFactory(SupportedDatabase.YugabyteDb));
        var gateway = new TableGateway<ExplicitIdentityEntity, int>(context);

        using var container = gateway.BuildUpsert(
            new ExplicitIdentityEntity { Id = 42, Value = "explicit identity" },
            context);

        Assert.Contains("OVERRIDING SYSTEM VALUE", container.Query.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildBatchUpsert_PostgreSql_WithWritableId_OverridesSystemIdentityValue()
    {
        // BatchUpsertAsync must match the single-row UpsertAsync contract
        // (BuildUpsert_PostgreSql_WithWritableId_OverridesSystemIdentityValue above) — the batch
        // INSERT path previously never emitted OVERRIDING SYSTEM VALUE at all, even on plain
        // PostgreSQL, so a batch upsert of >=2 entities failed with "cannot insert into column
        // defined as GENERATED ALWAYS AS IDENTITY" where the identical single-row upsert worked.
        var configuration = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=PostgreSql",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(
            configuration,
            new fakeDbFactory(SupportedDatabase.PostgreSql));
        var gateway = new TableGateway<ExplicitIdentityEntity, int>(context);

        var entities = new[]
        {
            new ExplicitIdentityEntity { Id = 42, Value = "explicit identity 1" },
            new ExplicitIdentityEntity { Id = 43, Value = "explicit identity 2" }
        };

        var containers = gateway.BuildBatchUpsert(entities, context);

        Assert.All(containers, c =>
            Assert.Contains("OVERRIDING SYSTEM VALUE", c.Query.ToString(), StringComparison.Ordinal));
    }

    [Table("upsert_entities")]
    private class ConflictEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("value", DbType.String)] public string Value { get; set; } = string.Empty;

        [PrimaryKey(1)]
        [Column("external_key", DbType.String)]
        public string ExternalKey { get; set; } = string.Empty;

        [Version]
        [Column("version", DbType.Int32)]
        public int Version { get; set; }
    }

    [Table("id_only_entities")]
    private class IdOnlyEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }

    [Table("explicit_identity_entities")]
    private class ExplicitIdentityEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }
}
