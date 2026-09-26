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

        // PostgreSQL 15+ MERGE: the right-hand side must read the target row explicitly.
        Assert.Contains("\"version\" = t.\"version\" + 1", sql);
        Assert.True(sql.Contains("ON CONFLICT") || sql.Contains("MERGE INTO"),
            "Expected Postgres upsert to use ON CONFLICT or MERGE.");
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

    // BP-117 (3.0 c58cb96 / a7ae9ef PG part). Single-row upsert emitted OVERRIDING SYSTEM VALUE
    // only for PostgreSql/AuroraPostgreSql via a product switch (YugabyteDB never got it) and the
    // batch upsert path never emitted it at all, so a GENERATED ALWAYS identity column rejected
    // the explicit id. PostgreSQL < 10 has no identity columns / OVERRIDING clause.
    private static DatabaseContext CreateUpsertContext(SupportedDatabase db, string? version = null)
    {
        var factory = new fakeDbFactory(db);
        if (version != null)
        {
            var connection = new fakeDbConnection { EmulatedProduct = db };
            connection.SetServerVersion(version);
            connection.SetScalarResultForCommand("SELECT version()", version);
            factory.Connections.Add(connection);
        }

        return new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={db}",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        }, factory);
    }

    private static readonly ExplicitIdentityEntity[] TwoExplicitIds =
    {
        new() { Id = 42, Value = "explicit identity 1" },
        new() { Id = 43, Value = "explicit identity 2" }
    };

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql, true)]
    [InlineData(SupportedDatabase.YugabyteDb, true)]
    [InlineData(SupportedDatabase.CockroachDb, false)]
    // CONFIRMED live: Spanner's PostgreSQL interface rejects the clause ("P0001: Statements with
    // OVERRIDING clauses are not supported").
    [InlineData(SupportedDatabase.Spanner, false)]
    public async Task BuildUpsert_WithWritableId_OverridingSystemValue_PerDialect(SupportedDatabase db, bool expected)
    {
        await using var context = CreateUpsertContext(db);
        Assert.Equal(db, context.Product);
        var gateway = new TableGateway<ExplicitIdentityEntity, int>(context);

        using var container = gateway.BuildUpsert(new ExplicitIdentityEntity { Id = 42, Value = "x" }, context);

        Assert.Equal(expected, container.Query.ToString().Contains("OVERRIDING SYSTEM VALUE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql, true)]
    [InlineData(SupportedDatabase.YugabyteDb, true)]
    [InlineData(SupportedDatabase.CockroachDb, false)]
    [InlineData(SupportedDatabase.Spanner, false)]
    public async Task BuildBatchUpsert_WithWritableId_OverridingSystemValue_PerDialect(SupportedDatabase db, bool expected)
    {
        await using var context = CreateUpsertContext(db);
        var gateway = new TableGateway<ExplicitIdentityEntity, int>(context);

        var containers = gateway.BuildBatchUpsert(TwoExplicitIds, context);

        Assert.NotEmpty(containers);
        Assert.All(containers, c => Assert.Equal(expected,
            c.Query.ToString().Contains("OVERRIDING SYSTEM VALUE", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("PostgreSQL 9.6.24 on x86_64-pc-linux-gnu", false)]
    [InlineData("PostgreSQL 14.10 on x86_64-pc-linux-gnu", true)]
    public async Task Upsert_PostgreSql_OverridingSystemValue_GatedOnVersion10(string version, bool expected)
    {
        await using var context = CreateUpsertContext(SupportedDatabase.PostgreSql, version);
        Assert.Contains(expected ? "14.10" : "9.6.24", context.DataSourceInfo.DatabaseProductVersion);
        var gateway = new TableGateway<ExplicitIdentityEntity, int>(context);

        using var single = gateway.BuildUpsert(new ExplicitIdentityEntity { Id = 42, Value = "x" }, context);
        var batch = gateway.BuildBatchUpsert(TwoExplicitIds, context);

        Assert.Equal(expected, single.Query.ToString().Contains("OVERRIDING SYSTEM VALUE", StringComparison.Ordinal));
        Assert.All(batch, c => Assert.Equal(expected,
            c.Query.ToString().Contains("OVERRIDING SYSTEM VALUE", StringComparison.Ordinal)));
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
