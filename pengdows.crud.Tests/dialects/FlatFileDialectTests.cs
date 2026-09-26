using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// FlatFileDialect capability decisions, each checked against the pengdows.flatfile provider
/// source (0.2.1-preview.1) rather than its README.
/// </summary>
public class FlatFileDialectTests
{
    private static FlatFileDialect Dialect() =>
        new(new fakeDbFactory(SupportedDatabase.FlatFile), NullLogger<FlatFileDialect>.Instance);

    [Fact]
    public void ApplicationNameSettingName_IsFlatFilesRealKeyword()
    {
        // Confirmed via pengdows.flatfile/FlatFileConnectionStringBuilder.cs's own
        // KeyApplicationName constant — a real, recognized keyword, not a guess.
        Assert.Equal("applicationName", Dialect().ApplicationNameSettingName);
    }

    [Fact]
    public void SupportsExternalPooling_IsFalse()
    {
        // FlatFile is a custom, in-process, file-based provider with no network handshake and no
        // real connection pool to configure — architecturally identical to why
        // DuckDbDialect.SupportsExternalPooling is false.
        Assert.False(Dialect().SupportsExternalPooling);
    }

    [Fact]
    public void GetReadOnlyConnectionParameter_IsFlatFilesRealReadOnlyKeyword()
    {
        // Confirmed via pengdows.flatfile/FlatFileConnectionStringBuilder.cs's own KeyReadOnly
        // constant and ReadOnly property (SetOrRemove(KeyReadOnly, value ? "true" : null)) — a
        // real, hard-enforced keyword: "any mutating statement (DML/DDL) is rejected immediately".
        Assert.Equal("readonly=true", Dialect().GetReadOnlyConnectionParameter());
    }

    // FlatFileConnection.Open takes ConnectionWriteLock for every non-readonly connection: one
    // writer per directory/file, a second in-process writer waits connectionTimeout then throws
    // TimeoutException. Same constraint as SQLite/DuckDB, so the same coercion policy.
    [Theory]
    [InlineData(DbMode.Best, DbMode.SingleWriter)]
    [InlineData(DbMode.Standard, DbMode.SingleWriter)]
    [InlineData(DbMode.PreventDatabaseUnload, DbMode.SingleWriter)]
    [InlineData(DbMode.SingleWriter, DbMode.SingleWriter)]
    [InlineData(DbMode.SingleConnection, DbMode.SingleConnection)]
    public void CoerceConnectionMode_UsesSingleWriterPolicy(DbMode requested, DbMode expected)
    {
        var (mode, _) = Dialect().CoerceConnectionMode(requested, "path=/tmp/db", isLocalDb: false);

        Assert.Equal(expected, mode);
    }

    [Fact]
    public void IsEmbeddedSingleWriterEngine_IsTrue()
    {
        Assert.True(Dialect().IsEmbeddedSingleWriterEngine);
    }

    // pengdows.sql/SqlParser.cs parses SAVEPOINT / RELEASE SAVEPOINT / ROLLBACK TO SAVEPOINT with a
    // regular or delimited identifier, and DefaultFlatFileQueryExecutor routes them to
    // FlatFileTransaction.Save/Release/Rollback(name) (FlatFileTransaction.SupportsSavepoints).
    [Fact]
    public void Savepoints_AreFullySupported_WithAnsiSyntax()
    {
        var d = Dialect();

        Assert.True(d.SupportsSavepoints);
        Assert.Equal(
            SavepointCapabilities.Create | SavepointCapabilities.Rollback | SavepointCapabilities.Release,
            d.SavepointCapabilities);
        Assert.Equal("SAVEPOINT \"sp1\"", d.GetSavepointSql("sp1"));
        Assert.Equal("ROLLBACK TO SAVEPOINT \"sp1\"", d.GetRollbackToSavepointSql("sp1"));
        Assert.Equal("RELEASE SAVEPOINT \"sp1\"", d.GetReleaseSavepointSql("sp1"));
    }

    [Fact]
    public async Task TransactionSavepoints_DoNotThrow()
    {
        var factory = new fakeDbFactory(SupportedDatabase.FlatFile);
        await using var context = new DatabaseContext("path=/tmp/db;EmulatedProduct=FlatFile", factory);
        await using var tx = context.BeginTransaction();

        await tx.SavepointAsync("sp1");
        await tx.RollbackToSavepointAsync("sp1");
        await tx.ReleaseSavepointAsync("sp1");
    }

    [Table("ff_upsert")]
    private sealed class FfUpsertEntity
    {
        [Id(true)] [Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
        [Version] [Column("ver", DbType.Int32)] public int Ver { get; set; }
    }

    private static DatabaseContext FlatFileContext() =>
        new("path=/tmp/db;EmulatedProduct=FlatFile", new fakeDbFactory(SupportedDatabase.FlatFile));

    // pengdows.sql/SqlParser.cs ParseMerge: MERGE INTO t USING (VALUES ...) AS s (cols) ON ...
    // WHEN MATCHED [AND cond] THEN UPDATE SET col = ... WHEN NOT MATCHED THEN INSERT; the SET target
    // is a bare column (ExpectIdentifier then Equals): "SET t.name = ..." fails with "Expected token
    // 'Equals' ... found 'Dot'". There is no ON CONFLICT / ON DUPLICATE KEY / RETURNING. All probed
    // against the real provider, including the version-guarded and multi-row VALUES forms.
    [Fact]
    public void Upsert_UsesMerge_WithBareUpdateTargets()
    {
        var d = Dialect();
        Assert.True(d.SupportsMerge);
        Assert.False(d.MergeUpdateRequiresTargetAlias);
        Assert.False(d.SupportsInsertOnConflict);
        Assert.False(d.SupportsOnDuplicateKey);
        Assert.False(d.SupportsInsertReturning);

        using var context = FlatFileContext();
        var gateway = new TableGateway<FfUpsertEntity, long>(context);
        using var sc = gateway.BuildUpsert(new FfUpsertEntity { Id = 1, Name = "a", Ver = 1 });
        var sql = sc.Query.ToString();

        Assert.StartsWith("MERGE INTO \"ff_upsert\" t USING (VALUES (:", sql);
        Assert.Contains("WHEN MATCHED AND t.\"ver\" = s.\"ver\" THEN UPDATE SET \"name\" = s.\"name\"", sql);
        Assert.DoesNotContain("SET t.", sql);
    }

    // pengdows.sql/SqlParser.cs has ParseOffset/ParseFetchFirst (OFFSET n ROWS FETCH {FIRST|NEXT}
    // n ROWS ONLY) and no LIMIT clause: "SELECT * FROM t LIMIT 1" fails with "Expected token
    // 'EndOfInput' ... found 'NumericLiteral'" (probed against the real provider).
    [Fact]
    public void Paging_IsOffsetFetchOnly()
    {
        var d = Dialect();

        Assert.True(d.SupportsOffsetFetch);
        Assert.False(d.SupportsLimitOffset);
        var query = new SqlQueryBuilder();
        d.AppendPaging(query, 10, 5);
        Assert.Equal(" OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY", query.ToString());
    }

    [Fact]
    public void GetNaturalKeyLookupQuery_UsesFetchFirst_NoLimit()
    {
        var d = Dialect();

        var sql = d.GetNaturalKeyLookupQuery("orders", "id", new[] { "order_code" }, new[] { ":p0" });

        Assert.Equal("SELECT \"id\" FROM \"orders\" WHERE \"order_code\" = :p0 FETCH FIRST 1 ROWS ONLY", sql);
    }

    // TransactionCharacteristicsExecutor applies SET TRANSACTION READ ONLY to the current
    // FlatFileTransaction, and DefaultFlatFileQueryExecutor then rejects every mutating statement
    // ("the transaction is READ ONLY"). The setting is per transaction (a new FlatFileTransaction
    // per BEGIN), so nothing leaks to the next transaction on the connection. This enforces
    // read-only even on a writer connection (SingleConnection mode), where readonly=true is absent.
    [Fact]
    public void SupportsReadOnlyTransactions_IsTrue()
    {
        Assert.True(Dialect().SupportsReadOnlyTransactions);
    }

    [Fact]
    public void TryEnterReadOnlyTransaction_ExecutesSetTransactionReadOnly()
    {
        var container = new Mock<ISqlContainer>(MockBehavior.Strict);
        container.Setup(c => c.ExecuteNonQueryAsync(CommandType.Text)).ReturnsAsync(0).Verifiable();
        container.Setup(c => c.Dispose()).Verifiable();
        var transaction = new Mock<ITransactionContext>(MockBehavior.Strict);
        transaction.Setup(t => t.CreateSqlContainer("SET TRANSACTION READ ONLY"))
            .Returns(container.Object)
            .Verifiable();

        Dialect().TryEnterReadOnlyTransaction(transaction.Object);

        container.Verify();
        transaction.Verify();
    }

    [Fact]
    public async Task TryEnterReadOnlyTransactionAsync_ExecutesSetTransactionReadOnly()
    {
        var container = new Mock<ISqlContainer>(MockBehavior.Strict);
        container.Setup(c => c.ExecuteNonQueryAsync(CommandType.Text, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0)
            .Verifiable();
        container.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        var transaction = new Mock<ITransactionContext>(MockBehavior.Strict);
        transaction.Setup(t => t.CreateSqlContainer("SET TRANSACTION READ ONLY"))
            .Returns(container.Object)
            .Verifiable();

        await Dialect().TryEnterReadOnlyTransactionAsync(transaction.Object, CancellationToken.None);

        container.Verify();
        transaction.Verify();
    }

    // pengdows.sql/SqlLexer.cs tokenizes ":name" as a NamedParameter and
    // BoundPredicateEvaluator.ResolveParameter matches it against DbParameter.ParameterName
    // (bare, case-insensitive). The old "positional ? only" README claim is stale.
    [Fact]
    public void SupportsNamedParameters_IsTrue_WithColonMarker()
    {
        var d = Dialect();

        Assert.True(d.SupportsNamedParameters);
        Assert.Equal(":", d.ParameterMarker);
        Assert.Equal(":w0", d.MakeParameterName("w0"));
    }

    // With positional parameters the base dialect applied the ODBC-style common conversions
    // (bool to Int16, Guid to string, DateTimeOffset to UTC DateTime). pengdows.flatfile's type
    // system (ClrTypeMap) has native bool/Guid/DateTimeOffset, and a BOOLEAN column rejects the
    // Int16 value 1 ("Value '1' is not valid for boolean column"), found live in the testbed.
    [Fact]
    public void CreateDbParameter_Boolean_StaysBoolean()
    {
        var p = Dialect().CreateDbParameter("b", DbType.Boolean, true);

        Assert.Equal(DbType.Boolean, p.DbType);
        Assert.Equal(true, p.Value);
    }

    [Fact]
    public void CreateDbParameter_Guid_StaysGuid()
    {
        var guid = Guid.NewGuid();

        var p = Dialect().CreateDbParameter("g", DbType.Guid, guid);

        Assert.Equal(DbType.Guid, p.DbType);
        Assert.Equal(guid, p.Value);
    }

    [Fact]
    public void CreateDbParameter_DateTimeOffset_KeepsOffset()
    {
        var value = new DateTimeOffset(2026, 9, 25, 10, 30, 0, TimeSpan.FromHours(-5));

        var p = Dialect().CreateDbParameter("d", DbType.DateTimeOffset, value);

        Assert.Equal(value, p.Value);
    }
}
