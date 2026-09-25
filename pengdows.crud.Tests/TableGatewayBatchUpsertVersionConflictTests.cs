using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// BP-202: TableGateway&lt;T,TId&gt;.BatchUpsertAsync only summed rows affected, so a stale
/// [Version] that the SQL's version guard skipped was silently swallowed. It now throws
/// ConcurrencyConflictException wherever the generated SQL carries a guard (per-entity MERGE,
/// ON CONFLICT DO UPDATE ... WHERE), and deliberately does not throw where it can't detect a
/// conflict (MySQL-family ON DUPLICATE KEY, Firebird UPDATE OR INSERT).
/// </summary>
public class TableGatewayBatchUpsertVersionConflictTests
{
    [Table("versioned_upsert_rows")]
    public class VersionedUpsertRow
    {
        [Id(true)][Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
        [Version][Column("version", DbType.Int32)] public int Version { get; set; }
        [LastUpdatedBy][Column("updated_by", DbType.String)] public string? UpdatedBy { get; set; }
    }

    private static DatabaseContext MakeContext(SupportedDatabase db, params int[] rowsAffected)
    {
        var factory = new fakeDbFactory(db);
        var connection = new fakeDbConnection();
        foreach (var r in rowsAffected)
        {
            connection.EnqueueNonQueryResult(r);
        }

        factory.Connections.Add(connection);
        var cs = db switch
        {
            SupportedDatabase.PostgreSql => "Host=localhost;EmulatedProduct=PostgreSql",
            SupportedDatabase.MySql => "Server=localhost;EmulatedProduct=MySql",
            SupportedDatabase.SqlServer => "Data Source=test;EmulatedProduct=SqlServer",
            SupportedDatabase.Firebird => "Data Source=test;EmulatedProduct=Firebird",
            _ => $"Data Source=test;EmulatedProduct={db}"
        };
        return new DatabaseContext(
            new DatabaseContextConfiguration { ConnectionString = cs, DbMode = DbMode.SingleConnection },
            factory, NullLoggerFactory.Instance);
    }

    private static List<VersionedUpsertRow> TwoRows() =>
    [
        new() { Id = 1, Name = "a", Version = 1 },
        new() { Id = 2, Name = "b", Version = 999 }
    ];

    [Fact]
    public async Task BatchUpsertAsync_SqlServerPerEntityMerge_StaleEntity_ThrowsAndRestoresAudit()
    {
        // SQL Server has no ON CONFLICT/ON DUPLICATE KEY, so each entity gets its own MERGE with a
        // WHEN MATCHED AND version guard: 0 rows from the second MERGE is a conflict on entity b.
        await using var context = MakeContext(SupportedDatabase.SqlServer, 1, 0);
        var gateway = new TableGateway<VersionedUpsertRow, int>(context, new StubAuditValueResolver("upsert-user"));
        var rows = TwoRows();

        var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => gateway.BatchUpsertAsync(rows, context).AsTask());

        Assert.Contains("id=2", ex.Message);
        Assert.Null(rows[1].UpdatedBy);
    }

    [Fact]
    public async Task BatchUpsertAsync_PostgresOnConflictChunk_PartialConflict_Throws()
    {
        // PostgreSQL batches the chunk into one INSERT ... ON CONFLICT DO UPDATE ... WHERE version
        // guard; 1 of 2 affected means the guard skipped a stale row.
        await using var context = MakeContext(SupportedDatabase.PostgreSql, 1);
        var gateway = new TableGateway<VersionedUpsertRow, int>(context, new StubAuditValueResolver("upsert-user"));

        var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => gateway.BatchUpsertAsync(TwoRows(), context).AsTask());

        Assert.Contains("cannot be individually identified", ex.Message);
    }

    [Fact]
    public async Task BatchUpsertAsync_MySqlOnDuplicateKey_ZeroAffected_DoesNotThrow()
    {
        // ON DUPLICATE KEY UPDATE has no version guard, and MySQL reports 0 affected for an upsert
        // that changed nothing — an ordinary no-op, not a conflict.
        await using var context = MakeContext(SupportedDatabase.MySql, 0);
        var gateway = new TableGateway<VersionedUpsertRow, int>(context, new StubAuditValueResolver("upsert-user"));

        var affected = await gateway.BatchUpsertAsync(TwoRows(), context);

        Assert.Equal(0, affected);
    }

    [Fact]
    public async Task BatchUpsertAsync_Firebird_ZeroAffected_DoesNotThrow()
    {
        // Firebird's UPDATE OR INSERT carries no version guard (single-entity UpsertAsync doesn't
        // throw there either), so 0 affected can't be read as a conflict.
        await using var context = MakeContext(SupportedDatabase.Firebird, 0, 0);
        var gateway = new TableGateway<VersionedUpsertRow, int>(context, new StubAuditValueResolver("upsert-user"));

        var affected = await gateway.BatchUpsertAsync(TwoRows(), context);

        Assert.Equal(0, affected);
    }

    [Fact]
    public async Task BatchUpsertAsync_AllRowsAffected_DoesNotThrow()
    {
        await using var context = MakeContext(SupportedDatabase.SqlServer, 1, 1);
        var gateway = new TableGateway<VersionedUpsertRow, int>(context, new StubAuditValueResolver("upsert-user"));

        var affected = await gateway.BatchUpsertAsync(TwoRows(), context);

        Assert.Equal(2, affected);
    }
}
