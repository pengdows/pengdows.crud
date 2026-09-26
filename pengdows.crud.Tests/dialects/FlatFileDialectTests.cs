using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
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
