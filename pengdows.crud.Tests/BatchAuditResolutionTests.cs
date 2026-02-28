using System;
using System.Collections.Generic;
using System.Data;
using Moq;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Verifies that batch operations resolve IAuditValueResolver.Resolve() exactly once
/// per batch call, not once per entity.
/// </summary>
public class BatchAuditResolutionTests
{
    [Table("audit_batch")]
    private class AuditBatchEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [CreatedBy]
        [Column("created_by", DbType.String)]
        public string CreatedBy { get; set; } = string.Empty;

        [CreatedOn]
        [Column("created_on", DbType.DateTime)]
        public DateTime CreatedOn { get; set; }

        [LastUpdatedBy]
        [Column("last_updated_by", DbType.String)]
        public string LastUpdatedBy { get; set; } = string.Empty;

        [LastUpdatedOn]
        [Column("last_updated_on", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }
    }

    private static readonly DateTime FixedUtcNow = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private static IDatabaseContext CreateContext(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        return new DatabaseContext($"Host=localhost;EmulatedProduct={db}", factory);
    }

    private static (Mock<IAuditValueResolver> mock, IAuditValues values) MakeMockResolver(string userId)
    {
        var values = new AuditValues { UserId = userId, UtcNow = FixedUtcNow };
        var mock = new Mock<IAuditValueResolver>();
        mock.Setup(r => r.Resolve()).Returns(values);
        return (mock, values);
    }

    private static List<AuditBatchEntity> MakeEntities(int count)
    {
        var list = new List<AuditBatchEntity>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new AuditBatchEntity { Name = $"entity-{i}" });
        }

        return list;
    }

    // ─── BuildBatchCreate ────────────────────────────────────────────────────

    [Fact]
    public void BuildBatchCreate_CallsResolveOnce_ForMultipleEntities()
    {
        var (mock, _) = MakeMockResolver("batch-user");
        var ctx = CreateContext(SupportedDatabase.Sqlite);
        var gateway = new TableGateway<AuditBatchEntity, int>(ctx, mock.Object);

        gateway.BuildBatchCreate(MakeEntities(5), ctx);

        mock.Verify(r => r.Resolve(), Times.Once());
    }

    [Fact]
    public void BuildBatchCreate_AllEntitiesGetSameAuditValues()
    {
        var (mock, values) = MakeMockResolver("batch-user");
        var ctx = CreateContext(SupportedDatabase.Sqlite);
        var gateway = new TableGateway<AuditBatchEntity, int>(ctx, mock.Object);
        var entities = MakeEntities(4);

        gateway.BuildBatchCreate(entities, ctx);

        foreach (var e in entities)
        {
            Assert.Equal("batch-user", e.CreatedBy);
            Assert.Equal("batch-user", e.LastUpdatedBy);
            Assert.Equal(FixedUtcNow, e.CreatedOn);
            Assert.Equal(FixedUtcNow, e.LastUpdatedOn);
        }
    }

    [Fact]
    public void BuildBatchCreate_SingleEntity_StillCallsResolveOnce()
    {
        var (mock, _) = MakeMockResolver("single-user");
        var ctx = CreateContext(SupportedDatabase.Sqlite);
        var gateway = new TableGateway<AuditBatchEntity, int>(ctx, mock.Object);

        gateway.BuildBatchCreate(MakeEntities(1), ctx);

        mock.Verify(r => r.Resolve(), Times.Once());
    }

    // ─── BuildBatchUpsert — ON CONFLICT path (PostgreSQL / CockroachDB) ───────

    [Fact]
    public void BuildBatchUpsert_PostgreSql_CallsResolveOnce_ForMultipleEntities()
    {
        var (mock, _) = MakeMockResolver("batch-user");
        var ctx = CreateContext(SupportedDatabase.PostgreSql);
        var gateway = new TableGateway<AuditBatchEntity, int>(ctx, mock.Object);

        gateway.BuildBatchUpsert(MakeEntities(5), ctx);

        mock.Verify(r => r.Resolve(), Times.Once());
    }

    [Fact]
    public void BuildBatchUpsert_PostgreSql_AllEntitiesGetSameAuditValues()
    {
        var (mock, _) = MakeMockResolver("pg-user");
        var ctx = CreateContext(SupportedDatabase.PostgreSql);
        var gateway = new TableGateway<AuditBatchEntity, int>(ctx, mock.Object);
        var entities = MakeEntities(3);

        gateway.BuildBatchUpsert(entities, ctx);

        foreach (var e in entities)
        {
            Assert.Equal("pg-user", e.CreatedBy);
            Assert.Equal(FixedUtcNow, e.CreatedOn);
        }
    }

    // ─── BuildBatchUpsert — ON DUPLICATE KEY path (MySQL / MariaDB) ──────────

    [Fact]
    public void BuildBatchUpsert_MySql_CallsResolveOnce_ForMultipleEntities()
    {
        var (mock, _) = MakeMockResolver("batch-user");
        var ctx = CreateContext(SupportedDatabase.MySql);
        var gateway = new TableGateway<AuditBatchEntity, int>(ctx, mock.Object);

        gateway.BuildBatchUpsert(MakeEntities(5), ctx);

        mock.Verify(r => r.Resolve(), Times.Once());
    }

    [Fact]
    public void BuildBatchUpsert_MySql_AllEntitiesGetSameAuditValues()
    {
        var (mock, _) = MakeMockResolver("mysql-user");
        var ctx = CreateContext(SupportedDatabase.MySql);
        var gateway = new TableGateway<AuditBatchEntity, int>(ctx, mock.Object);
        var entities = MakeEntities(3);

        gateway.BuildBatchUpsert(entities, ctx);

        foreach (var e in entities)
        {
            Assert.Equal("mysql-user", e.LastUpdatedBy);
            Assert.Equal(FixedUtcNow, e.LastUpdatedOn);
        }
    }
}
