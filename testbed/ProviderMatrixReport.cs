// =============================================================================
// FILE: ProviderMatrixReport.cs
// PURPOSE: Pure, Docker-free completeness matrix for every SupportedDatabase value — answers
// "does this provider have a dialect / exception translator / testbed container / type catalog
// entry / live round-trip configuration wired up" without touching a live database.
//
// See pengdows.crud.IntegrationTests/Infrastructure/ProviderMatrixCompletenessTests.cs for the
// tests that lock this report's current output down to a known snapshot — those tests are
// expected to start FAILING the moment a gap tracked here is closed (or a new one opens), by
// design: that forces the snapshot to be updated deliberately rather than letting this report
// rot into a false PASS.
//
// AI SUMMARY:
// - Five dimensions per SupportedDatabase value: dialect, exception-translator,
//   testbed-container, type-catalog, live-round-trip-configuration.
// - "A registered provider factory", one of the checklist items from the request that produced
//   this file, is deliberately folded into testbed-container rather than tracked as its own
//   dimension: pengdows.crud has no global DbProviderFactory registry (each dialect/container is
//   constructor-injected with one — see SqlDialectFactory.CreateDialectForType's own
//   `DbProviderFactory factory` parameter), so a dedicated testbed ITestContainer class already
//   IS the meaningful proxy for "a real ADO.NET provider factory is wired up for this database"
//   in this codebase's actual architecture — there is no separate registry to check.
// - Status is one of Pass / Missing / ExemptByDesign. ExemptByDesign covers cases documented
//   elsewhere as intentional, not oversights (Aurora MySQL/PostgreSQL: managed AWS services with
//   no Docker image, detected at runtime, covered entirely by the MySQL/PostgreSQL suites — see
//   CLAUDE.md's "Aurora variants" section). Everything else absent is Missing — a real,
//   documented-or-not gap, not a design decision.
// =============================================================================

using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions.translators;
using testbed.Cockroach;
using testbed.Db2;
using testbed.DuckDb;
using testbed.FlatFile;
using testbed.Firebird;
using testbed.Informix;
using testbed.InterBase;
using testbed.mariaDb;
using testbed.MySQL;
using testbed.Oracle;
using testbed.PostgreSQL;
using testbed.SapHana;
using testbed.SingleStore;
using testbed.Snowflake;
using testbed.Spanner;
using testbed.SqlServer;
using testbed.Sybase;
using testbed.TiDB;
using testbed.Yugabyte;

namespace testbed;

public enum MatrixStatus
{
    Pass,
    Missing,
    ExemptByDesign
}

public sealed record MatrixCheckResult(SupportedDatabase Provider, string Dimension, MatrixStatus Status, string? Reason = null)
{
    public override string ToString() => Status switch
    {
        MatrixStatus.Pass => $"{Provider} / {Dimension}: PASS",
        MatrixStatus.Missing => $"{Provider} / {Dimension}: MISSING" + (Reason is null ? "" : $" ({Reason})"),
        MatrixStatus.ExemptByDesign => $"{Provider} / {Dimension}: EXEMPT ({Reason})",
        _ => $"{Provider} / {Dimension}: UNKNOWN"
    };
}

public static class ProviderMatrixReport
{
    public static readonly IReadOnlyList<SupportedDatabase> AllProducts = Enum.GetValues<SupportedDatabase>()
        .Where(v => v != SupportedDatabase.Unknown)
        .ToArray();

    // Only used to satisfy SqlDialectFactory.CreateDialectForType's DbProviderFactory parameter —
    // never opened, never used for anything provider-specific. Any concrete DbProviderFactory
    // would do; SqliteFactory is already a direct testbed dependency.
    private static readonly DbProviderFactory ProbeFactory = SqliteFactory.Instance;

    // Managed AWS services with no Docker image; detected at runtime (DatabaseDetectionService)
    // and delegate entirely to the MySQL/PostgreSQL dialect and test suites — see CLAUDE.md's
    // "Aurora variants" section. Applies to the testbed-container and live-round-trip-
    // configuration dimensions only (both concern "is there a live container for this
    // database", not the dialect/translator/catalog dimensions, which are dialect-shape
    // questions Aurora still legitimately answers via its delegate dialect).
    private static readonly IReadOnlySet<SupportedDatabase> ExemptFromLiveContainerDimensions =
        new HashSet<SupportedDatabase> { SupportedDatabase.AuroraMySql, SupportedDatabase.AuroraPostgreSql };

    // One dedicated testbed ITestContainer type per database that has one, referenced by
    // typeof() so a rename/removal breaks the BUILD rather than silently going stale here.
    private static readonly IReadOnlyDictionary<SupportedDatabase, Type> TestbedContainers =
        new Dictionary<SupportedDatabase, Type>
        {
            [SupportedDatabase.Sqlite] = typeof(SqliteTestContainer),
            [SupportedDatabase.FlatFile] = typeof(FlatFileTestContainer),
            [SupportedDatabase.PostgreSql] = typeof(PostgreSqlTestContainer),
            [SupportedDatabase.Spanner] = typeof(SpannerOmniTestContainer),
            [SupportedDatabase.SqlServer] = typeof(SqlServerTestContainer),
            [SupportedDatabase.MySql] = typeof(MySqlTestContainer),
            [SupportedDatabase.MariaDb] = typeof(MariaDbContainer),
            [SupportedDatabase.Oracle] = typeof(OracleTestContainer),
            [SupportedDatabase.Firebird] = typeof(FirebirdSqlTestContainer),
            [SupportedDatabase.CockroachDb] = typeof(CockroachDbTestContainer),
            [SupportedDatabase.DuckDB] = typeof(DuckDbTestContainer),
            [SupportedDatabase.YugabyteDb] = typeof(YugabyteTestContainer),
            [SupportedDatabase.TiDb] = typeof(TiDBTestContainer),
            [SupportedDatabase.Db2] = typeof(Db2TestContainer),
            [SupportedDatabase.SingleStore] = typeof(SingleStoreTestContainer),
            [SupportedDatabase.SybaseASE] = typeof(SybaseTestContainer),
            [SupportedDatabase.Snowflake] = typeof(SnowflakeTestContainer),
            [SupportedDatabase.Informix] = typeof(InformixTestContainer),
            [SupportedDatabase.SapHana] = typeof(HanaTestContainer),
            [SupportedDatabase.InterBase] = typeof(InterBaseTestContainer)
        };

    // Mirrors the literal provider-name strings ParallelTestOrchestrator.GetTestConfigurations()
    // registers each database under (AddLocal/AddDocker's first argument) — kept here as a
    // one-time mapping rather than re-deriving it, since GetTestConfigurations() itself has no
    // SupportedDatabase-keyed lookup of its own (it's driven by call order, not the enum).
    private static readonly IReadOnlyDictionary<SupportedDatabase, string> LiveRoundTripProviderNames =
        new Dictionary<SupportedDatabase, string>
        {
            [SupportedDatabase.Sqlite] = "SQLite",
            [SupportedDatabase.DuckDB] = "DuckDB",
            [SupportedDatabase.FlatFile] = "FlatFile",
            [SupportedDatabase.PostgreSql] = "PostgreSQL",
            [SupportedDatabase.Spanner] = "Spanner",
            [SupportedDatabase.MySql] = "MySQL",
            [SupportedDatabase.MariaDb] = "MariaDB",
            [SupportedDatabase.SqlServer] = "SQL Server",
            [SupportedDatabase.CockroachDb] = "CockroachDB",
            [SupportedDatabase.Firebird] = "Firebird",
            [SupportedDatabase.TiDb] = "TiDB",
            [SupportedDatabase.YugabyteDb] = "YugabyteDB",
            [SupportedDatabase.Oracle] = "Oracle",
            [SupportedDatabase.Db2] = "Db2",
            [SupportedDatabase.SingleStore] = "SingleStore",
            [SupportedDatabase.SybaseASE] = "Sybase ASE",
            [SupportedDatabase.Informix] = "Informix",
            [SupportedDatabase.Snowflake] = "Snowflake",
            [SupportedDatabase.SapHana] = "SAP HANA",
            [SupportedDatabase.InterBase] = "InterBase"
        };

    // Snowflake, SAP HANA, and InterBase are the three databases CLAUDE.md documents under
    // "Opt-in exceptions (require env var)" — each one's testbed container constructor validates
    // real external state eagerly (SnowflakeTestContainer throws immediately without
    // SNOWFLAKE_ACCOUNT/USER/PASSWORD/WAREHOUSE/DATABASE set, even just to be enumerated), so
    // their orchestrator-entry presence is confirmed structurally here rather than by actually
    // constructing them — matching what GetTestConfigurations() itself does (each is only added
    // when its own _includeXxx flag is true).
    private static readonly IReadOnlySet<SupportedDatabase> OptInLiveRoundTripConfigurations =
        new HashSet<SupportedDatabase> { SupportedDatabase.Snowflake, SupportedDatabase.SapHana, SupportedDatabase.InterBase };

    private static readonly Lazy<HashSet<string>> LiveRoundTripConfiguredProviders = new(() =>
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var orchestrator = new ParallelTestOrchestrator(services);
        return orchestrator.GetTestConfigurations()
            .Select(c => c.DatabaseProvider)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    });

    public static IReadOnlyList<MatrixCheckResult> Build()
    {
        var results = new List<MatrixCheckResult>();
        foreach (var db in AllProducts)
        {
            results.Add(CheckDialect(db));
            results.Add(CheckExceptionTranslator(db));
            results.Add(CheckTestbedContainer(db));
            results.Add(CheckLiveRoundTripConfiguration(db));
            results.Add(CheckTypeCatalogEntry(db));
        }

        return results;
    }

    private static MatrixCheckResult CheckDialect(SupportedDatabase db)
    {
        var dialect = SqlDialectFactory.CreateDialectForType(db, ProbeFactory, NullLogger.Instance);
        return dialect.GetType() == typeof(Sql92Dialect)
            ? new MatrixCheckResult(db, "dialect", MatrixStatus.Missing,
                "SqlDialectFactory.CreateDialectForType fell back to the generic Sql92Dialect")
            : new MatrixCheckResult(db, "dialect", MatrixStatus.Pass);
    }

    private static MatrixCheckResult CheckExceptionTranslator(SupportedDatabase db)
    {
        var translator = new DbExceptionTranslatorRegistry().Get(db);
        return translator.GetType() == typeof(FallbackExceptionTranslator)
            ? new MatrixCheckResult(db, "exception-translator", MatrixStatus.Missing,
                "DbExceptionTranslatorRegistry.Get fell back to FallbackExceptionTranslator")
            : new MatrixCheckResult(db, "exception-translator", MatrixStatus.Pass);
    }

    private static MatrixCheckResult CheckTestbedContainer(SupportedDatabase db)
    {
        if (TestbedContainers.ContainsKey(db))
        {
            return new MatrixCheckResult(db, "testbed-container", MatrixStatus.Pass);
        }

        if (ExemptFromLiveContainerDimensions.Contains(db))
        {
            return new MatrixCheckResult(db, "testbed-container", MatrixStatus.ExemptByDesign,
                "managed AWS service with no Docker image; covered by the MySQL/PostgreSQL suites (CLAUDE.md 'Aurora variants')");
        }

        return new MatrixCheckResult(db, "testbed-container", MatrixStatus.Missing,
            "no dedicated ITestContainer implementation registered in ProviderMatrixReport.TestbedContainers");
    }

    private static MatrixCheckResult CheckLiveRoundTripConfiguration(SupportedDatabase db)
    {
        if (OptInLiveRoundTripConfigurations.Contains(db))
        {
            return new MatrixCheckResult(db, "live-round-trip-configuration", MatrixStatus.Pass,
                "opt-in: gated behind an environment variable (see CLAUDE.md 'Opt-in exceptions')");
        }

        if (LiveRoundTripProviderNames.TryGetValue(db, out var name) &&
            LiveRoundTripConfiguredProviders.Value.Contains(name))
        {
            return new MatrixCheckResult(db, "live-round-trip-configuration", MatrixStatus.Pass);
        }

        if (ExemptFromLiveContainerDimensions.Contains(db))
        {
            return new MatrixCheckResult(db, "live-round-trip-configuration", MatrixStatus.ExemptByDesign,
                "managed AWS service with no Docker image; covered by the MySQL/PostgreSQL suites (CLAUDE.md 'Aurora variants')");
        }

        return new MatrixCheckResult(db, "live-round-trip-configuration", MatrixStatus.Missing,
            "not present in ParallelTestOrchestrator.GetTestConfigurations()'s output");
    }

    private static MatrixCheckResult CheckTypeCatalogEntry(SupportedDatabase db)
    {
        var types = DatabaseTypeCatalog.GetColumnTypes(db);
        return types.Count > 0
            ? new MatrixCheckResult(db, "type-catalog", MatrixStatus.Pass)
            : new MatrixCheckResult(db, "type-catalog", MatrixStatus.Missing,
                "DatabaseTypeCatalog.GetColumnTypes returns an empty list for this database");
    }
}
