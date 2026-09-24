using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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
/// B20: the multi-row batch UPDATE (dialects with SupportsBatchUpdate, e.g. PostgreSQL) wrote
/// CreatedBy/CreatedOn from the in-memory entity and copied the client's [Version] verbatim —
/// no increment, no version check — so a stale batch update silently overwrote newer data.
/// Creation audit columns and the version are now never in the batch SET, and versioned entities
/// are updated row by row, which increments, checks the version, and throws on a conflict.
/// </summary>
public class TableGatewayBatchUpdateVersionAuditTests
{
    [Table("versioned_rows")]
    public class VersionedRow
    {
        [Id(true)][Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
        [Version][Column("version", DbType.Int32)] public int Version { get; set; }
    }

    [Table("audited_rows")]
    public class AuditedRow
    {
        [Id(true)][Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
        [CreatedBy][Column("created_by", DbType.String)] public string CreatedBy { get; set; } = "";
        [CreatedOn][Column("created_on", DbType.DateTime)] public DateTime CreatedOn { get; set; }
        [LastUpdatedBy][Column("updated_by", DbType.String)] public string UpdatedBy { get; set; } = "";
        [LastUpdatedOn][Column("updated_on", DbType.DateTime)] public DateTime UpdatedOn { get; set; }
    }

    private static DatabaseContext PostgresContext(fakeDbFactory? factory = null) =>
        new($"Host=localhost;EmulatedProduct={SupportedDatabase.PostgreSql}",
            factory ?? new fakeDbFactory(SupportedDatabase.PostgreSql));

    [Fact]
    public void BuildBatchUpdate_Versioned_IncrementsAndChecksVersionPerRow()
    {
        using var context = PostgresContext();
        var gateway = new TableGateway<VersionedRow, int>(context);

        var containers = gateway.BuildBatchUpdate(new List<VersionedRow>
        {
            new() { Id = 1, Name = "a", Version = 5 },
            new() { Id = 2, Name = "b", Version = 7 }
        });

        Assert.Equal(2, containers.Count);
        foreach (var sql in containers.Select(c => c.Query.ToString()))
        {
            Assert.Contains("\"version\" = \"version\" + 1", sql);
            Assert.Contains("\"version\" = ", sql[sql.IndexOf(" WHERE ", StringComparison.Ordinal)..]);
        }
    }

    [Fact]
    public async Task BatchUpdateAsync_Versioned_StaleRow_ThrowsConcurrencyConflict()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var connection = new fakeDbConnection();
        connection.EnqueueNonQueryResult(1);
        connection.EnqueueNonQueryResult(0); // second row's version no longer matches
        factory.Connections.Add(connection);
        await using var context = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = $"Host=localhost;EmulatedProduct={SupportedDatabase.PostgreSql}",
                DbMode = DbMode.SingleConnection
            },
            factory, NullLoggerFactory.Instance);
        var gateway = new TableGateway<VersionedRow, int>(context);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
            await gateway.BatchUpdateAsync(new List<VersionedRow>
            {
                new() { Id = 1, Name = "a", Version = 5 },
                new() { Id = 2, Name = "b", Version = 7 }
            }));
    }

    [Fact]
    public void BuildBatchUpdate_NeverWritesCreationAuditColumns()
    {
        using var context = PostgresContext();
        var gateway = new TableGateway<AuditedRow, int>(context, new StubAuditValueResolver("batch-user"));

        var containers = gateway.BuildBatchUpdate(new List<AuditedRow>
        {
            new() { Id = 1, Name = "a", CreatedBy = "someone-else", CreatedOn = new DateTime(2020, 1, 1) },
            new() { Id = 2, Name = "b", CreatedBy = "someone-else", CreatedOn = new DateTime(2020, 1, 1) }
        });

        var sql = containers.Single().Query.ToString();
        Assert.DoesNotContain("\"created_by\"", sql);
        Assert.DoesNotContain("\"created_on\"", sql);
        Assert.Contains("\"updated_by\"", sql);
        Assert.Contains("\"updated_on\"", sql);
    }
}
