#region

using System;
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

    private static string FileConnectionString(SupportedDatabase db) => db switch
    {
        SupportedDatabase.Sqlite => "Data Source=file.db",
        SupportedDatabase.DuckDB => "Data Source=file.duckdb",
        // Access has no in-memory mode at all (unlike Sqlite/DuckDB's ":memory:") — every
        // connection is file-based, so it only participates in the file-based cases below, not
        // EmbeddedDialect_IsolatedInMemory_ForcesSingleConnection above.
        SupportedDatabase.Access => "Provider=Microsoft.ACE.OLEDB.16.0;Data Source=file.accdb;",
        _ => throw new ArgumentOutOfRangeException(nameof(db))
    };

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.DuckDB)]
    [InlineData(SupportedDatabase.Access)]
    public void EmbeddedDialect_FileOrSharedMemory_Best_ResolvesToSingleWriter(SupportedDatabase db)
    {
        var dialect = CreateDialect(db);
        var cs = FileConnectionString(db);
        var (mode, reason) = dialect.CoerceConnectionMode(DbMode.Best, cs, isLocalDb: false);
        Assert.Equal(DbMode.SingleWriter, mode);
        Assert.Contains("Best selects SingleWriter", reason);
    }

    [Fact]
    public void Sqlite_FileOrSharedMemory_UnsafeExplicitModes_CoerceToSingleWriter()
    {
        // Sqlite never opts into allowStandard — Standard/PreventDatabaseUnload both stay
        // hard-coerced, unlike DuckDB/Access below.
        var dialect = CreateDialect(SupportedDatabase.Sqlite);
        var cs = FileConnectionString(SupportedDatabase.Sqlite);

        var (standardMode, _) = dialect.CoerceConnectionMode(DbMode.Standard, cs, isLocalDb: false);
        var (preventUnloadMode, _) = dialect.CoerceConnectionMode(DbMode.PreventDatabaseUnload, cs, isLocalDb: false);

        Assert.Equal(DbMode.SingleWriter, standardMode);
        Assert.Equal(DbMode.SingleWriter, preventUnloadMode);
    }

    [Theory]
    [InlineData(SupportedDatabase.DuckDB)]
    [InlineData(SupportedDatabase.Access)]
    public void EmbeddedDialect_FileOrSharedMemory_PreventDatabaseUnload_StillCoercesToSingleWriter(
        SupportedDatabase db)
    {
        // PreventDatabaseUnload's "keep an idle-unload-prone server attachment alive" purpose
        // doesn't apply to a file-based embedded engine — only Standard was carved out for
        // DuckDB/Access, not PreventDatabaseUnload.
        var dialect = CreateDialect(db);
        var cs = FileConnectionString(db);

        var (mode, _) = dialect.CoerceConnectionMode(DbMode.PreventDatabaseUnload, cs, isLocalDb: false);

        Assert.Equal(DbMode.SingleWriter, mode);
    }

    [Theory]
    [InlineData(SupportedDatabase.DuckDB)]
    [InlineData(SupportedDatabase.Access)]
    public void EmbeddedDialect_FileOrSharedMemory_ExplicitStandard_IsHonored(SupportedDatabase db)
    {
        // Both DuckDB and Access document support for concurrent connections/writers, so an
        // explicit Standard request is honored (Best still defaults to SingleWriter) — unlike
        // Sqlite, which stays hard-coerced regardless. DatabaseContext.WarnOnModeMismatch layers
        // an evidence-backed risk warning on top of this (see DbModeCoercionLoggingTests and each
        // dialect's DescribeStandardModeRisk override); this test only locks down the coercion
        // decision itself.
        var dialect = CreateDialect(db);
        var cs = FileConnectionString(db);

        var (mode, reason) = dialect.CoerceConnectionMode(DbMode.Standard, cs, isLocalDb: false);

        Assert.Equal(DbMode.Standard, mode);
        Assert.Equal(string.Empty, reason);
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.DuckDB)]
    [InlineData(SupportedDatabase.Access)]
    public void EmbeddedDialect_FileOrSharedMemory_SafeExplicitModes_AreHonored(SupportedDatabase db)
    {
        var dialect = CreateDialect(db);
        var cs = FileConnectionString(db);

        var (singleWriterMode, _) = dialect.CoerceConnectionMode(DbMode.SingleWriter, cs, isLocalDb: false);
        var (singleConnectionMode, _) = dialect.CoerceConnectionMode(DbMode.SingleConnection, cs, isLocalDb: false);

        Assert.Equal(DbMode.SingleWriter, singleWriterMode);
        Assert.Equal(DbMode.SingleConnection, singleConnectionMode);
    }

    /// <summary>
    /// Default-deny invariant over the WHOLE enum, generalizing the hand-picked
    /// <c>InlineData</c> lists above the same way <c>DialectDetectInMemoryKindTests</c>'
    /// equivalent test does: regardless of which specific category a database falls into
    /// (full client-server, embedded single-writer, topology-forced), asking for
    /// <see cref="DbMode.Best"/> must always resolve to a concrete, usable mode and must never
    /// throw — a dialect that left this ambiguous (returned <c>Best</c> unchanged, or threw)
    /// would break <see cref="DatabaseContext"/> construction for every caller who didn't pass an
    /// explicit mode, which is the common case. A hand-picked list only proves what someone
    /// thought to test; this proves it for every value, including any future one, automatically.
    /// </summary>
    [Fact]
    public void EveryDatabase_BestMode_ResolvesToConcreteNonBestMode()
    {
        foreach (SupportedDatabase db in Enum.GetValues(typeof(SupportedDatabase)))
        {
            var dialect = CreateDialect(db);

            var (mode, reason) = dialect.CoerceConnectionMode(DbMode.Best, "Data Source=test;", isLocalDb: false);

            Assert.NotEqual(DbMode.Best, mode);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }
    }

    [Fact]
    public void SqlServer_LocalDb_ExplicitStandard_IsHonored()
    {
        // PreventDatabaseUnload's sentinel only matters for a workload that actually goes idle
        // long enough to trigger LocalDB's auto-shutdown; a caller who knows their workload stays
        // busy can opt out via an explicit Standard request. Best still auto-selects
        // PreventDatabaseUnload (see SqlServer_NotLocalDb_BehavesAsOrdinaryFullServer's sibling
        // LocalDb Best coverage in DbModeCoercionLoggingTests).
        var dialect = CreateDialect(SupportedDatabase.SqlServer);
        var (mode, reason) = dialect.CoerceConnectionMode(DbMode.Standard, "Server=(localdb)\\mssqllocaldb", isLocalDb: true);
        Assert.Equal(DbMode.Standard, mode);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void SqlServer_LocalDb_OtherExplicitModes_ForcePreventDatabaseUnload()
    {
        // Only Standard was carved out for LocalDB — every other explicit request (and Best) still
        // forces PreventDatabaseUnload, matching the pre-existing unconditional behavior.
        var dialect = CreateDialect(SupportedDatabase.SqlServer);
        var (mode, reason) = dialect.CoerceConnectionMode(DbMode.SingleWriter, "Server=(localdb)\\mssqllocaldb", isLocalDb: true);
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
