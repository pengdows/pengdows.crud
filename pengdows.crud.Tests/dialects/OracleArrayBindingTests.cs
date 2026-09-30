using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

// FEAT-005: OracleCommand.ArrayBindCount lets one parameterized, single-row-shaped INSERT bind
// each column to an array of per-row values instead of a multi-row VALUES list — Oracle's answer
// to the same problem docs/planning/bulk-loading-design.md's rejected FEAT-012 tried to solve for
// every other engine via a streaming importer. ArrayBindCount is not on the generic
// DbCommand/DbParameter surface, so it's reached via the same reflection pattern
// ApplyConnectionSettingsCore already uses for StatementCacheSize: no hard package reference from
// pengdows.crud to Oracle.ManagedDataAccess.Core.
public sealed class OracleArrayBindingTests
{
    // Property discovered via reflection; class name contains "Oracle" so
    // GetType().FullName?.Contains("Oracle") matches, mirroring
    // OracleDialectAdditionalTests.FakeOracleConnection's exact pattern for the connection side.
    private sealed class FakeOracleCommand : fakeDbCommand
    {
        public int ArrayBindCount { get; set; } = -1;
    }

    private static OracleDialect CreateOracleDialect()
    {
        return new OracleDialect(new fakeDbFactory(SupportedDatabase.Oracle), NullLogger<OracleDialect>.Instance);
    }

    [Fact]
    public void SupportsArrayBinding_TrueForOracle()
    {
        Assert.True(CreateOracleDialect().SupportsArrayBinding);
    }

    [Fact]
    public void SupportsArrayBinding_FalseForOtherDialects()
    {
        var postgres = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger<PostgreSqlDialect>.Instance);
        var sqlServer = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger<SqlServerDialect>.Instance);

        Assert.False(postgres.SupportsArrayBinding);
        Assert.False(sqlServer.SupportsArrayBinding);
    }

    [Fact]
    public void ConfigureArrayBinding_SetsArrayBindCount_ViaReflection()
    {
        var dialect = CreateOracleDialect();
        DbCommand cmd = new FakeOracleCommand();

        dialect.ConfigureArrayBinding(cmd, 7);

        Assert.Equal(7, ((FakeOracleCommand)cmd).ArrayBindCount);
    }

    [Fact]
    public void ConfigureArrayBinding_CommandWithoutArrayBindCountProperty_DoesNotThrow()
    {
        var dialect = CreateOracleDialect();
        DbCommand cmd = new fakeDbCommand(); // plain fake — no ArrayBindCount, name doesn't contain "Oracle"

        var ex = Record.Exception(() => dialect.ConfigureArrayBinding(cmd, 3));

        Assert.Null(ex);
    }

    [Fact]
    public void ConfigureArrayBinding_NonOracleDialect_IsNoOp()
    {
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger<PostgreSqlDialect>.Instance);
        DbCommand cmd = new FakeOracleCommand();

        dialect.ConfigureArrayBinding(cmd, 5);

        // Base SqlDialect.ConfigureArrayBinding is a no-op; the command's own default (-1) is untouched.
        Assert.Equal(-1, ((FakeOracleCommand)cmd).ArrayBindCount);
    }
}
