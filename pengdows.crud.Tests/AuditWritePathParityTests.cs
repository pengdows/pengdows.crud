using System;
using System.Collections.Generic;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DRY-016 found the write paths disagreeing on audit fields when no IAuditValueResolver is given:
/// CreateAsync set time-only fields and refused user fields (InvalidOperationException, as
/// documented), while the upsert and batch preparation skipped audit handling entirely, leaving
/// [CreatedOn]/[LastUpdatedOn] at default and writing (or, on the gateway's single-row upsert,
/// dropping) [CreatedBy] silently. Every write path now behaves as CreateAsync.
/// </summary>
public class AuditWritePathParityTests
{
    [Table("timed")]
    public class Timed
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [PrimaryKey] [Column("code", DbType.String)] public string Code { get; set; } = "c";
        [Column("name", DbType.String)] public string Name { get; set; } = "n";
        [CreatedOn] [Column("created_on", DbType.DateTime)] public DateTime CreatedOn { get; set; }
        [LastUpdatedOn] [Column("updated_on", DbType.DateTime)] public DateTime UpdatedOn { get; set; }
    }

    [Table("timed_pk")]
    public class TimedPk
    {
        [PrimaryKey] [Column("code", DbType.String)] public string Code { get; set; } = "c";
        [Column("name", DbType.String)] public string Name { get; set; } = "n";
        [CreatedOn] [Column("created_on", DbType.DateTime)] public DateTime CreatedOn { get; set; }
        [LastUpdatedOn] [Column("updated_on", DbType.DateTime)] public DateTime UpdatedOn { get; set; }
    }

    [Table("owned")]
    public class Owned
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [PrimaryKey] [Column("code", DbType.String)] public string Code { get; set; } = "c";
        [CreatedBy] [Column("created_by", DbType.String)] public string? CreatedBy { get; set; }
    }

    [Table("updated")]
    public class Updated
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "n";
        [LastUpdatedBy] [Column("updated_by", DbType.String)] public string? UpdatedBy { get; set; }
    }

    [Table("updated_pk")]
    public class UpdatedPk
    {
        [PrimaryKey] [Column("code", DbType.String)] public string Code { get; set; } = "c";
        [Column("name", DbType.String)] public string Name { get; set; } = "n";
        [LastUpdatedBy] [Column("updated_by", DbType.String)] public string? UpdatedBy { get; set; }
    }

    [Table("owned_pk")]
    public class OwnedPk
    {
        [PrimaryKey] [Column("code", DbType.String)] public string Code { get; set; } = "c";
        [Column("name", DbType.String)] public string Name { get; set; } = "n";
        [CreatedBy] [Column("created_by", DbType.String)] public string? CreatedBy { get; set; }
    }

    public static IEnumerable<object[]> Databases() =>
        new[] { new object[] { SupportedDatabase.PostgreSql }, new object[] { SupportedDatabase.SqlServer }, new object[] { SupportedDatabase.MySql } };

    private static DatabaseContext Context(SupportedDatabase db) =>
        new($"Data Source=t;EmulatedProduct={db}", new fakeDbFactory(db));

    private static IEnumerable<(string Path, Action<T> Build)> Paths<T>(Func<T, ISqlContainer> create, Func<T, ISqlContainer> upsert,
        Func<IReadOnlyList<T>, IReadOnlyList<ISqlContainer>> batchCreate, Func<IReadOnlyList<T>, IReadOnlyList<ISqlContainer>> batchUpsert)
    {
        yield return ("BuildCreate", e => create(e).Dispose());
        yield return ("BuildUpsert", e => upsert(e).Dispose());
        yield return ("BuildBatchCreate", e => Dispose(batchCreate(new[] { e, e })));
        yield return ("BuildBatchUpsert", e => Dispose(batchUpsert(new[] { e, e })));
    }

    private static void Dispose(IReadOnlyList<ISqlContainer> containers)
    {
        foreach (var c in containers)
        {
            c.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public void TimeOnlyAuditFields_AreSetOnEveryWritePath_WithoutAResolver(SupportedDatabase db)
    {
        using var context = Context(db);
        var gateway = new TableGateway<Timed, int>(context);
        var pk = new PrimaryKeyTableGateway<TimedPk>(context);
        var unset = new List<string>();

        foreach (var (path, build) in Paths<Timed>(e => gateway.BuildCreate(e), e => gateway.BuildUpsert(e), e => gateway.BuildBatchCreate(e), e => gateway.BuildBatchUpsert(e)))
        {
            var entity = new Timed { Id = 1 };
            build(entity);
            if (entity.CreatedOn == default || entity.UpdatedOn == default)
            {
                unset.Add("TableGateway." + path);
            }
        }

        foreach (var (path, build) in Paths<TimedPk>(e => pk.BuildCreate(e), e => pk.BuildUpsert(e), e => pk.BuildBatchCreate(e), e => pk.BuildBatchUpsert(e)))
        {
            var entity = new TimedPk();
            build(entity);
            if (entity.CreatedOn == default || entity.UpdatedOn == default)
            {
                unset.Add("PrimaryKeyTableGateway." + path);
            }
        }

        Assert.True(unset.Count == 0, "left unset: " + string.Join(", ", unset));
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public void UserAuditFields_WithoutAResolver_FailEveryWritePath(SupportedDatabase db)
    {
        using var context = Context(db);
        var gateway = new TableGateway<Owned, int>(context);
        var pk = new PrimaryKeyTableGateway<OwnedPk>(context);
        var silent = new List<string>();

        foreach (var (path, build) in Paths<Owned>(e => gateway.BuildCreate(e), e => gateway.BuildUpsert(e), e => gateway.BuildBatchCreate(e), e => gateway.BuildBatchUpsert(e)))
        {
            if (Record.Exception(() => build(new Owned { Id = 1 })) is not InvalidOperationException)
            {
                silent.Add("TableGateway." + path);
            }
        }

        foreach (var (path, build) in Paths<OwnedPk>(e => pk.BuildCreate(e), e => pk.BuildUpsert(e), e => pk.BuildBatchCreate(e), e => pk.BuildBatchUpsert(e)))
        {
            if (Record.Exception(() => build(new OwnedPk())) is not InvalidOperationException)
            {
                silent.Add("PrimaryKeyTableGateway." + path);
            }
        }

        Assert.True(silent.Count == 0, "no InvalidOperationException: " + string.Join(", ", silent));
    }

    // A batch update wrote [LastUpdatedBy] as NULL without a resolver; UpdateAsync refuses.
    [Theory]
    [MemberData(nameof(Databases))]
    public void UserAuditFields_WithoutAResolver_FailBatchUpdate(SupportedDatabase db)
    {
        using var context = Context(db);
        var gateway = new TableGateway<Updated, int>(context);
        var pk = new PrimaryKeyTableGateway<UpdatedPk>(context);

        Assert.Throws<InvalidOperationException>(() => gateway.BuildBatchUpdate(new[] { new Updated { Id = 1 }, new Updated { Id = 2 } }));
        Assert.Throws<InvalidOperationException>(() => pk.BuildBatchUpdate(new[] { new UpdatedPk { Code = "a" }, new UpdatedPk { Code = "b" } }));
    }
}
