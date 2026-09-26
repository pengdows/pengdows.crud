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
    public void EmbeddedDialect_FileOrSharedMemory_PreventDatabaseUnload_CoercesToSingleWriter(SupportedDatabase db)
    {
        var dialect = CreateDialect(db);
        var cs = db == SupportedDatabase.Sqlite ? "Data Source=file.db" : "Data Source=file.duckdb";

        var (preventUnloadMode, _) = dialect.CoerceConnectionMode(DbMode.PreventDatabaseUnload, cs, isLocalDb: false);

        Assert.Equal(DbMode.SingleWriter, preventUnloadMode);
    }

    // SQLite stays hard-coerced; DuckDB (documented as supporting concurrent connections) honors
    // an explicit Standard, with a risk warning (3.0 b356af1).
    [Theory]
    [InlineData(SupportedDatabase.Sqlite, DbMode.SingleWriter)]
    [InlineData(SupportedDatabase.DuckDB, DbMode.Standard)]
    public void EmbeddedDialect_File_ExplicitStandard(SupportedDatabase db, DbMode expected)
    {
        var dialect = CreateDialect(db);
        var cs = db == SupportedDatabase.Sqlite ? "Data Source=file.db" : "Data Source=file.duckdb";

        var (standardMode, _) = dialect.CoerceConnectionMode(DbMode.Standard, cs, isLocalDb: false);

        Assert.Equal(expected, standardMode);
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

    // Where Best resolves to PreventDatabaseUnload, that is a default, not a mandate: a busy
    // deployment may never drain its pool, so an explicit Standard (or any other explicit mode)
    // must be honored as-is (maintainer policy, 2026-09-25).
    [Fact]
    public void SqlServer_LocalDb_Best_SelectsPreventDatabaseUnload()
    {
        var dialect = CreateDialect(SupportedDatabase.SqlServer);
        var (mode, reason) = dialect.CoerceConnectionMode(DbMode.Best, "Server=(localdb)\\mssqllocaldb", isLocalDb: true);
        Assert.Equal(DbMode.PreventDatabaseUnload, mode);
        Assert.Contains("PreventDatabaseUnload", reason);
    }

    [Fact]
    public void SqlServer_LocalDb_ExplicitStandard_IsHonored()
    {
        var dialect = CreateDialect(SupportedDatabase.SqlServer);
        var (mode, _) = dialect.CoerceConnectionMode(DbMode.Standard, "Server=(localdb)\\mssqllocaldb", isLocalDb: true);
        Assert.Equal(DbMode.Standard, mode);
    }

    // Matches 3.0 (b356af1): only Standard is opted out; the single-connection modes have no
    // purpose on LocalDB and still resolve to PreventDatabaseUnload.
    [Theory]
    [InlineData(DbMode.PreventDatabaseUnload)]
    [InlineData(DbMode.SingleWriter)]
    [InlineData(DbMode.SingleConnection)]
    public void SqlServer_LocalDb_OtherExplicitModes_UsePreventDatabaseUnload(DbMode requested)
    {
        var dialect = CreateDialect(SupportedDatabase.SqlServer);
        var (mode, _) = dialect.CoerceConnectionMode(requested, "Server=(localdb)\\mssqllocaldb", isLocalDb: true);
        Assert.Equal(DbMode.PreventDatabaseUnload, mode);
    }

    // Firebird's idle-unload reconnect cost was measured live (the BP-206 testbed probe: the
    // sentinel saved ~7-10ms per cold checkout), so Best keeps a sentinel per pool.
    [Theory]
    [InlineData("Server=localhost;Database=/data/test.fdb")]
    [InlineData("Database=C:/data/test.fdb;ServerType=Embedded")]
    public void Firebird_Best_SelectsPreventDatabaseUnload(string connectionString)
    {
        var dialect = CreateDialect(SupportedDatabase.Firebird);
        var (mode, reason) = dialect.CoerceConnectionMode(DbMode.Best, connectionString, isLocalDb: false);
        Assert.Equal(DbMode.PreventDatabaseUnload, mode);
        Assert.Contains("PreventDatabaseUnload", reason);
    }

    [Theory]
    [InlineData(DbMode.Standard)]
    [InlineData(DbMode.PreventDatabaseUnload)]
    [InlineData(DbMode.SingleWriter)]
    [InlineData(DbMode.SingleConnection)]
    public void Firebird_ExplicitMode_IsHonored(DbMode requested)
    {
        var dialect = CreateDialect(SupportedDatabase.Firebird);
        var (mode, _) = dialect.CoerceConnectionMode(requested, "Server=localhost;Database=/data/test.fdb", isLocalDb: false);
        Assert.Equal(requested, mode);
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
