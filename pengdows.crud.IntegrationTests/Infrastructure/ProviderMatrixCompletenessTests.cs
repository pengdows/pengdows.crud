using pengdows.crud.enums;
using testbed;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// Locks the current state of <see cref="ProviderMatrixReport"/> down to a known snapshot, per
/// database, for every dimension it tracks. No live database or Docker container is touched —
/// this only exercises registration/configuration surfaces (dialect factory, exception
/// translator registry, testbed container map, orchestrator configuration list, type catalog).
///
/// These tests are EXPECTED to start failing the moment a tracked gap is closed (or a new one
/// opens) — that is the point: it forces whoever closes a gap to update the corresponding
/// "KnownMissing*" set here deliberately, rather than the report quietly drifting out of date.
/// </summary>
public class ProviderMatrixCompletenessTests
{
    private readonly ITestOutputHelper _output;

    public ProviderMatrixCompletenessTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void EveryProduct_HasADialectRegistered()
    {
        var offenders = ProviderMatrixReport.Build()
            .Where(r => r.Dimension == "dialect" && r.Status != MatrixStatus.Pass)
            .ToList();

        foreach (var offender in offenders)
        {
            _output.WriteLine(offender.ToString());
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryProduct_HasAnExceptionTranslatorRegistered()
    {
        var offenders = ProviderMatrixReport.Build()
            .Where(r => r.Dimension == "exception-translator" && r.Status != MatrixStatus.Pass)
            .ToList();

        foreach (var offender in offenders)
        {
            _output.WriteLine(offender.ToString());
        }

        Assert.Empty(offenders);
    }

    // SingleStore's testbed-container gap (SingleStoreTestContainer, wired into
    // ParallelTestOrchestrator) was closed live in this session — confirmed via
    // `dotnet run --project testbed -- --only SingleStore` against a real
    // ghcr.io/singlestore-labs/singlestoredb-dev container. Nothing left unaccounted for here.
    private static readonly IReadOnlySet<SupportedDatabase> KnownMissingTestbedContainers =
        new HashSet<SupportedDatabase>();

    private static readonly IReadOnlySet<SupportedDatabase> KnownExemptTestbedContainers =
        new HashSet<SupportedDatabase> { SupportedDatabase.AuroraMySql, SupportedDatabase.AuroraPostgreSql };

    [Fact]
    public void ProviderMatrix_TestbedContainerCoverage_MatchesKnownState()
    {
        var report = ProviderMatrixReport.Build().Where(r => r.Dimension == "testbed-container").ToList();
        foreach (var r in report)
        {
            _output.WriteLine(r.ToString());
        }

        AssertStatusSetMatches(report, MatrixStatus.Missing, KnownMissingTestbedContainers, "testbed-container MISSING");
        AssertStatusSetMatches(report, MatrixStatus.ExemptByDesign, KnownExemptTestbedContainers, "testbed-container EXEMPT");
    }

    // Closed alongside the testbed-container gap above — SingleStore now has a
    // ParallelTestOrchestrator.GetTestConfigurations() entry.
    private static readonly IReadOnlySet<SupportedDatabase> KnownMissingLiveRoundTripConfiguration =
        new HashSet<SupportedDatabase>();

    private static readonly IReadOnlySet<SupportedDatabase> KnownExemptLiveRoundTripConfiguration =
        new HashSet<SupportedDatabase> { SupportedDatabase.AuroraMySql, SupportedDatabase.AuroraPostgreSql };

    [Fact]
    public void ProviderMatrix_LiveRoundTripConfigurationCoverage_MatchesKnownState()
    {
        var report = ProviderMatrixReport.Build().Where(r => r.Dimension == "live-round-trip-configuration").ToList();
        foreach (var r in report)
        {
            _output.WriteLine(r.ToString());
        }

        AssertStatusSetMatches(report, MatrixStatus.Missing, KnownMissingLiveRoundTripConfiguration, "live-round-trip-configuration MISSING");
        AssertStatusSetMatches(report, MatrixStatus.ExemptByDesign, KnownExemptLiveRoundTripConfiguration, "live-round-trip-configuration EXEMPT");
    }

    // The 15 databases DatabaseTypeCatalog.cs's own file header documents as "deliberately left
    // unpopulated rather than guessed" (14 named there) plus FlatFile, which that header doesn't
    // mention at all — a genuine gap in the catalog's own scope statement, not a duplicate entry.
    private static readonly IReadOnlySet<SupportedDatabase> KnownMissingTypeCatalogEntries =
        new HashSet<SupportedDatabase>
        {
            SupportedDatabase.PostgreSql,
            SupportedDatabase.SqlServer,
            SupportedDatabase.Oracle,
            SupportedDatabase.Firebird,
            SupportedDatabase.CockroachDb,
            SupportedDatabase.MariaDb,
            SupportedDatabase.MySql,
            SupportedDatabase.Sqlite,
            SupportedDatabase.DuckDB,
            SupportedDatabase.YugabyteDb,
            SupportedDatabase.TiDb,
            SupportedDatabase.Snowflake,
            SupportedDatabase.AuroraMySql,
            SupportedDatabase.AuroraPostgreSql,
            SupportedDatabase.FlatFile,
        };

    [Fact]
    public void ProviderMatrix_TypeCatalogCoverage_MatchesKnownState()
    {
        var report = ProviderMatrixReport.Build().Where(r => r.Dimension == "type-catalog").ToList();
        foreach (var r in report)
        {
            _output.WriteLine(r.ToString());
        }

        AssertStatusSetMatches(report, MatrixStatus.Missing, KnownMissingTypeCatalogEntries, "type-catalog MISSING");
    }

    /// <summary>
    /// Prints the full completeness matrix to test output in the "Provider / dimension: STATUS"
    /// format — a human-readable snapshot for whoever's deciding what to work on next, without
    /// needing to run every other test here individually.
    /// </summary>
    [Fact]
    public void ProviderMatrix_PrintsFullCompletenessReport()
    {
        foreach (var line in ProviderMatrixReport.Build())
        {
            _output.WriteLine(line.ToString());
        }
    }

    private static void AssertStatusSetMatches(
        IEnumerable<MatrixCheckResult> report,
        MatrixStatus status,
        IReadOnlySet<SupportedDatabase> expected,
        string label)
    {
        var actual = report.Where(r => r.Status == status).Select(r => r.Provider).ToHashSet();

        Assert.True(actual.SetEquals(expected),
            $"{label} set changed. Expected: [{string.Join(", ", expected.OrderBy(p => p.ToString()))}]. " +
            $"Actual: [{string.Join(", ", actual.OrderBy(p => p.ToString()))}].");
    }
}
