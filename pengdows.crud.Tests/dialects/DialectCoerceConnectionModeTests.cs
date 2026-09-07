#region

using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// ISqlDialect.CoerceConnectionMode is the single source of truth for "which DbMode should this
/// database actually use", including what DbMode.Best resolves to. DatabaseContext.CoerceMode now
/// just calls this and logs whatever (Mode, Reason) comes back — it owns no per-database rules of
/// its own. See CLAUDE.md's "Adding a New Database" checklist: a new client-server database needs
/// no override at all here (the base default already honors any explicit mode and resolves Best to
/// Standard); only a database with real mode restrictions (embedded engines, LocalDB) overrides it.
/// </summary>
public class DialectCoerceConnectionModeTests
{
    private static ISqlDialect CreateDialect(SupportedDatabase db) =>
        SqlDialectFactory.CreateDialectForType(db, new fakeDbFactory(db), NullLogger.Instance);

    [Fact]
    public void FullServerDialect_Best_ResolvesToStandard_WithFullServerReason()
    {
        var dialect = CreateDialect(SupportedDatabase.PostgreSql);
        var (mode, reason) = dialect.CoerceConnectionMode(DbMode.Best, "Server=localhost", isLocalDb: false);
        Assert.Equal(DbMode.Standard, mode);
        Assert.Contains("Full server", reason);
    }

    [Fact]
    public void FullServerDialect_ExplicitMode_IsHonored()
    {
        var dialect = CreateDialect(SupportedDatabase.PostgreSql);
        var (mode, _) = dialect.CoerceConnectionMode(DbMode.SingleWriter, "Server=localhost", isLocalDb: false);
        Assert.Equal(DbMode.SingleWriter, mode);
    }

    [Fact]
    public void UnknownDialect_Best_ResolvesToStandard_WithUnknownProviderReason()
    {
        var dialect = CreateDialect(SupportedDatabase.Unknown);
        var (mode, reason) = dialect.CoerceConnectionMode(DbMode.Best, "Server=localhost", isLocalDb: false);
        Assert.Equal(DbMode.Standard, mode);
        Assert.Contains("Unknown provider", reason);
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.DuckDB)]
    public void EmbeddedDialect_IsolatedInMemory_ForcesSingleConnection(SupportedDatabase db)
    {
        var dialect = CreateDialect(db);
        var cs = db == SupportedDatabase.Sqlite ? "Data Source=:memory:" : "Data Source=:memory:";
        var (mode, reason) = dialect.CoerceConnectionMode(DbMode.Standard, cs, isLocalDb: false);
        Assert.Equal(DbMode.SingleConnection, mode);
        Assert.Contains("Isolated in-memory", reason);
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.DuckDB)]
    public void EmbeddedDialect_FileOrSharedMemory_Best_ResolvesToSingleWriter(SupportedDatabase db)
    {
        var dialect = CreateDialect(db);
        var cs = db == SupportedDatabase.Sqlite ? "Data Source=file.db" : "Data Source=file.duckdb";
        var (mode, reason) = dialect.CoerceConnectionMode(DbMode.Best, cs, isLocalDb: false);
        Assert.Equal(DbMode.SingleWriter, mode);
        Assert.Contains("Best selects SingleWriter", reason);
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.DuckDB)]
    public void EmbeddedDialect_FileOrSharedMemory_UnsafeExplicitModes_CoerceToSingleWriter(SupportedDatabase db)
    {
        var dialect = CreateDialect(db);
        var cs = db == SupportedDatabase.Sqlite ? "Data Source=file.db" : "Data Source=file.duckdb";

        var (standardMode, _) = dialect.CoerceConnectionMode(DbMode.Standard, cs, isLocalDb: false);
        var (preventUnloadMode, _) = dialect.CoerceConnectionMode(DbMode.PreventDatabaseUnload, cs, isLocalDb: false);

        Assert.Equal(DbMode.SingleWriter, standardMode);
        Assert.Equal(DbMode.SingleWriter, preventUnloadMode);
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.DuckDB)]
    public void EmbeddedDialect_FileOrSharedMemory_SafeExplicitModes_AreHonored(SupportedDatabase db)
    {
        var dialect = CreateDialect(db);
        var cs = db == SupportedDatabase.Sqlite ? "Data Source=file.db" : "Data Source=file.duckdb";

        var (singleWriterMode, _) = dialect.CoerceConnectionMode(DbMode.SingleWriter, cs, isLocalDb: false);
        var (singleConnectionMode, _) = dialect.CoerceConnectionMode(DbMode.SingleConnection, cs, isLocalDb: false);

        Assert.Equal(DbMode.SingleWriter, singleWriterMode);
        Assert.Equal(DbMode.SingleConnection, singleConnectionMode);
    }

    [Fact]
    public void SqlServer_LocalDb_ForcesPreventDatabaseUnload_RegardlessOfRequestedMode()
    {
        var dialect = CreateDialect(SupportedDatabase.SqlServer);
        var (mode, reason) = dialect.CoerceConnectionMode(DbMode.Standard, "Server=(localdb)\\mssqllocaldb", isLocalDb: true);
        Assert.Equal(DbMode.PreventDatabaseUnload, mode);
        Assert.Contains("LocalDB requires PreventDatabaseUnload", reason);
    }

    [Fact]
    public void SqlServer_NotLocalDb_BehavesAsOrdinaryFullServer()
    {
        var dialect = CreateDialect(SupportedDatabase.SqlServer);
        var (mode, reason) = dialect.CoerceConnectionMode(DbMode.Best, "Server=prod", isLocalDb: false);
        Assert.Equal(DbMode.Standard, mode);
        Assert.Contains("Full server", reason);
    }
}
