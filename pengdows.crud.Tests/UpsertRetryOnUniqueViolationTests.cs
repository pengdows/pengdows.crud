using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A MERGE (or Firebird's UPDATE OR INSERT) is not atomic against a concurrent insert of the same key: two callers on
/// one new key both take the insert branch and one fails with a unique violation. Proven on live Oracle, Db2 and
/// Firebird (ConcurrentUpsertTests, 16 callers: Oracle 5/16, Db2 9/16, Firebird 15/16). Re-running the statement is
/// the fix there: the row now exists, so it matches. Databases whose upsert is already atomic (PostgreSQL's ON
/// CONFLICT, SQL Server's HOLDLOCK MERGE) do not retry: a unique violation there is a different unique index.
/// </summary>
public class UpsertRetryOnUniqueViolationTests
{
    [Table("retry_rows")]
    public class Row
    {
        [Id] [Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
    }

    [Table("retry_pairs")]
    public class PkRow
    {
        [PrimaryKey(1)] [Column("code", DbType.String)] public string Code { get; set; } = "";
        [Column("name", DbType.String)] public string Name { get; set; } = "";
    }

    private sealed class ProviderDbException : DbException
    {
        private readonly string? _sqlState;

        public ProviderDbException(string message, int number, string? sqlState = null) : base(message)
        {
            Number = number;
            _sqlState = sqlState;
        }

        public int Number { get; }
        public override string? SqlState => _sqlState;
    }

    private static DbException UniqueViolation(SupportedDatabase db) => db switch
    {
        SupportedDatabase.Oracle => new ProviderDbException("ORA-00001: unique constraint (X.PK) violated", 1),
        SupportedDatabase.Db2 => new ProviderDbException("SQL0803N  One or more values in the INSERT statement", -803, "23505"),
        SupportedDatabase.Firebird => new ProviderDbException(
            "violation of PRIMARY or UNIQUE KEY constraint \"INTEG_2\" on table \"retry_rows\"", 335544665),
        SupportedDatabase.PostgreSql => new ProviderDbException("duplicate key value violates unique constraint", 0, "23505"),
        SupportedDatabase.SqlServer => new ProviderDbException("Violation of PRIMARY KEY constraint", 2627),
        _ => throw new NotSupportedException(db.ToString())
    };

    private static (DatabaseContext Context, fakeDbConnection Connection) Create(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        var connection = new fakeDbConnection
        {
            EmulatedProduct = db,
            ConnectionString = $"Data Source=test;EmulatedProduct={db}"
        };
        factory.Connections.Add(connection);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={db}",
            DbMode = DbMode.SingleConnection
        }, factory);
        return (context, connection);
    }

    // fakeDb records only the statements that ran to completion, so a retried failure shows as one.
    private static int Upserts(fakeDbConnection connection) =>
        connection.ExecutedNonQueryTexts.Count(t =>
            t.Contains("MERGE", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("UPDATE OR INSERT", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase));

    [Theory]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Db2)]
    [InlineData(SupportedDatabase.Firebird)]
    public async Task SingleRowUpsert_UniqueViolationOnTheFirstAttempt_IsRetriedOnce(SupportedDatabase db)
    {
        var (context, connection) = Create(db);
        await using var _ = context;
        var gateway = new TableGateway<Row, long>(context);
        connection.EnqueueTransientNonQueryFailures(UniqueViolation(db));

        var rows = await gateway.UpsertAsync(new Row { Id = 1, Name = "n" });

        Assert.Equal(1, rows);
        Assert.Equal(1, Upserts(connection));
    }

    [Theory]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Db2)]
    [InlineData(SupportedDatabase.Firebird)]
    public async Task PrimaryKeyGatewayUpsert_UniqueViolationOnTheFirstAttempt_IsRetriedOnce(SupportedDatabase db)
    {
        var (context, connection) = Create(db);
        await using var _ = context;
        var gateway = new PrimaryKeyTableGateway<PkRow>(context);
        connection.EnqueueTransientNonQueryFailures(UniqueViolation(db));

        var rows = await gateway.UpsertAsync(new PkRow { Code = "a", Name = "n" });

        Assert.Equal(1, rows);
        Assert.Equal(1, Upserts(connection));
    }

    [Theory]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Db2)]
    [InlineData(SupportedDatabase.Firebird)]
    public async Task SingleRowUpsert_APersistentUniqueViolation_IsRethrownAfterOneRetry(SupportedDatabase db)
    {
        var (context, connection) = Create(db);
        await using var _ = context;
        var gateway = new TableGateway<Row, long>(context);
        // Every attempt fails (a few are queued because the database may run other statements, such as Firebird's
        // rollback after an error, between the attempts): the violation reaches the caller, so the retry is bounded.
        connection.EnqueueTransientNonQueryFailures(Enumerable.Range(0, 6).Select(_ => (Exception)UniqueViolation(db)).ToArray());

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            async () => await gateway.UpsertAsync(new Row { Id = 1, Name = "n" }));

        Assert.Equal(0, Upserts(connection));
    }

    [Theory]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Db2)]
    [InlineData(SupportedDatabase.Firebird)]
    public async Task BatchUpsert_UniqueViolationOnAContainer_IsRetriedOnce(SupportedDatabase db)
    {
        var (context, connection) = Create(db);
        await using var _ = context;
        var gateway = new TableGateway<Row, long>(context);
        var rows = new List<Row> { new() { Id = 1, Name = "a" }, new() { Id = 2, Name = "b" } };
        connection.EnqueueTransientNonQueryFailures(UniqueViolation(db));

        await gateway.BatchUpsertAsync(rows);

        // Both containers completed: the failed one on its retry.
        Assert.Equal(rows.Count, Upserts(connection));
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    public async Task AnAtomicUpsert_DoesNotRetryAUniqueViolation(SupportedDatabase db)
    {
        var (context, connection) = Create(db);
        await using var _ = context;
        var gateway = new TableGateway<Row, long>(context);
        connection.EnqueueTransientNonQueryFailures(UniqueViolation(db));

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            async () => await gateway.UpsertAsync(new Row { Id = 1, Name = "n" }));
    }
}
