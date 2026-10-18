using System;
using System.IO;
using System.Linq;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// What the external audit of 2.0.6 found the documentation did not say (G-02, G-03, G-07, G-10, the schema qualifier, I1), pinned
/// so the statements stay where a reader looks for them.
/// </summary>
public sealed class AuditDocumentationTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "pengdows.crud.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(Root(), relative.Replace('/', Path.DirectorySeparatorChar)));

    // G-02: the default wait is documented where the setting is declared, not only in the pooling guide.
    [Fact]
    public void G02_PoolAcquireTimeout_ExplainsItsDefaultAndWhenToRaiseIt()
    {
        var declaration = Read("pengdows.crud.abstractions/configuration/IDatabaseContextConfiguration.cs");
        var property = declaration.Substring(0, declaration.IndexOf("TimeSpan PoolAcquireTimeout", StringComparison.Ordinal));
        var xml = property.Substring(property.LastIndexOf("/// <summary>", StringComparison.Ordinal));

        Assert.Contains("5 s", xml, StringComparison.Ordinal);
        Assert.Contains("slow", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("connection-pooling.md", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void G02_ThePoolingGuide_StatesTheDefaultDecidesWhetherASpikeIsSurvived()
    {
        var guide = Read("docs/connection-pooling.md");

        Assert.Contains("The default wait decides whether a spike is survived", guide, StringComparison.Ordinal);
    }

    // G-03: leak-safety covers completed and governed paths; an abandoned lease keeps its permit, and there is no finalizer.
    [Theory]
    [InlineData("llms.txt")]
    [InlineData("docs/core-invariants.md")]
    public void G03_AbandonedLeasesKeepTheirPermit_AndThereIsNoFinalizer(string file)
    {
        var text = Read(file);

        Assert.Contains("no finalizer", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PoolSaturatedException", text, StringComparison.Ordinal);
    }

    // G-07: already documented; pinned.
    [Fact]
    public void G07_UnspecifiedDateTimeIsUtc_IsDocumented()
    {
        var text = Read("docs/utc-and-time.md");

        Assert.Contains("Unspecified means UTC", text, StringComparison.Ordinal);
    }

    // G-10: an opaque version is compared on update but an upsert cannot detect a stale one.
    [Fact]
    public void G10_OpaqueVersions_AreNotGuardedOnUpsert()
    {
        var text = Read("docs/entity-mapping.md");

        Assert.Contains("upsert", text[text.IndexOf("opaque, database-generated version token", StringComparison.Ordinal)..],
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not detect a stale opaque version", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SchemaQualifier_IsDroppedWhereTheDatabaseHasNoNamespaces()
    {
        var text = Read("docs/entity-mapping.md");

        Assert.Contains("no namespaces", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SupportsNamespaces", text, StringComparison.Ordinal);
    }

    // I1: the resolver never lets RCSI satisfy SafeNonBlockingReads; SQL Server needs snapshot isolation.
    [Fact]
    public void I1_SqlServerNeedsRowVersioning_SnapshotIsolationOrRcsi()
    {
        var text = Read("docs/transactions.md");

        Assert.Contains("ALLOW_SNAPSHOT_ISOLATION", text, StringComparison.Ordinal);
        Assert.Contains("READ_COMMITTED_SNAPSHOT", text, StringComparison.Ordinal);
        Assert.Contains("statement-level MVCC", text, StringComparison.Ordinal);
    }

    // G-08: a blocking acquire parks its thread for up to the acquire timeout; what was and was not measured is stated.
    [Fact]
    public void G08_ThePoolingGuide_SaysABlockingWaitHoldsItsThreadAndWhatWasMeasured()
    {
        var text = Read("docs/connection-pooling.md");

        Assert.Contains("holds its thread", text, StringComparison.Ordinal);
        Assert.Contains("PoolAcquireTimeout", text, StringComparison.Ordinal);
        Assert.Contains("PostgreSqlSyncWaiterStarvationTests", text, StringComparison.Ordinal);
        Assert.Contains("about 0.9 to 1.0 s", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not measured against real I/O", text, StringComparison.Ordinal);
    }

    // The 2.0.6 fixes are documented where a reader looks: the changelog, the release notes and the future-work tracker.
    [Fact]
    public void TheAuditFixes_AreInTheChangelogAndReleaseNotes()
    {
        var changelog = Read("CHANGELOG.md");
        var notes = Read("docs/release-notes-2.0.6.md");
        var future = Read("docs/FUTURE_WORK.md");

        Assert.Contains("DefineUniqueKey", changelog, StringComparison.Ordinal);
        Assert.Contains("docs/facts.json", changelog, StringComparison.Ordinal);
        Assert.Contains("READ_COMMITTED_SNAPSHOT", changelog, StringComparison.Ordinal);
        Assert.Contains("fakeDbFactory.CreateConnection", changelog, StringComparison.Ordinal);
        Assert.Contains("## Isolation profiles", notes, StringComparison.Ordinal);
        Assert.Contains("FakeDbPoolBoundaryStressTests", notes, StringComparison.Ordinal);
        Assert.Contains("NoMockedProviderTypesTests", notes, StringComparison.Ordinal);
        Assert.Contains("Coercion registry, stage 2", future, StringComparison.Ordinal);
        Assert.Contains("homegrown mocking library", future, StringComparison.Ordinal);
    }

    // Decided 2026-10-18: snapshot isolation and RCSI are read once when the context is created (documented, not re-detected),
    // and the other audit items that were left out of 2.0.6 are kept in one place for 3.0.
    [Fact]
    public void TheTransactionsGuide_SaysSnapshotAndRcsiAreReadOnceAtStartup()
    {
        var text = Read("docs/transactions.md");

        Assert.Contains("read once when the context is created", text, StringComparison.Ordinal);
        Assert.Contains("restart", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FutureWork_KeepsEveryItemLeftOutOf206_ForThirtyOh()
    {
        var text = Read("docs/FUTURE_WORK.md");
        var section = text.Substring(text.IndexOf("## Left out of 2.0.6, for 3.0", StringComparison.Ordinal));

        foreach (var item in new[]
                 {
                     "Virtual time", "Coercion registry", "GC backstop", "PoolAcquireTimeout default", "Unspecified DateTime",
                     "MERGE model", "CI-recorded provenance", "Stricter upsert"
                 })
        {
            Assert.Contains(item, section, StringComparison.Ordinal);
        }
    }

    // The 2026-10-18 benchmark results are recorded where a reader looks, with what they show.
    [Fact]
    public void TheReleaseNotes_RecordTheUpsertAndEqualFootingBenchmarks()
    {
        var notes = Read("docs/release-notes-2.0.6.md");
        var section = notes.Substring(notes.IndexOf("## Performance", StringComparison.Ordinal));

        Assert.Contains("PostgreSqlUpsertBenchmarks", section, StringComparison.Ordinal);
        Assert.Contains("SqlServerUpsertBenchmarks", section, StringComparison.Ordinal);
        Assert.Contains("HOLDLOCK", section, StringComparison.Ordinal);
        Assert.Contains("SqlServerEqualFootingBenchmarks", section, StringComparison.Ordinal);
        Assert.Contains("PostgreSqlEqualFootingBenchmarks", section, StringComparison.Ordinal);
    }

    // Decided 2026-10-18: 2.0.6 is the last 2.0 release, so every open design item is a 3.0 item, none "next round" or "decide".
    [Theory]
    [InlineData("SESSION-001")]
    [InlineData("SQLITE-006")]
    [InlineData("SQLITE-007")]
    [InlineData("POISON-001")]
    [InlineData("POOL-001")]
    [InlineData("POOL-002")]
    [InlineData("POOL-003")]
    [InlineData("VAR-002")]
    public void EveryOpenDesignItem_IsMarkedForThirtyOh(string id)
    {
        var text = Read("docs/FUTURE_WORK.md");
        var row = text.Split('\n').First(l => l.StartsWith("| " + id + " |", StringComparison.Ordinal));

        Assert.Contains("3.0", row.Substring(row.LastIndexOf("**", StringComparison.Ordinal) + 1), StringComparison.Ordinal);
        Assert.DoesNotContain("next round |", row, StringComparison.Ordinal);
        Assert.DoesNotContain("Open (decide)", row, StringComparison.Ordinal);
        Assert.DoesNotContain("Open, needs a setting", row, StringComparison.Ordinal);
        Assert.DoesNotContain("Open, maintainer decision", row, StringComparison.Ordinal);
    }

    [Fact]
    public void FutureWork_SaysThisIsTheLastTwoOhRelease()
    {
        Assert.Contains("2.0.6 is the last 2.0 release", Read("docs/FUTURE_WORK.md"), StringComparison.Ordinal);
    }

    // SQLITE-008, measured 2026-10-18: the writer turnstile stays on by default on a WAL file; the cost and the trade are written down.
    [Fact]
    public void TheWriterTurnstileDecision_IsRecordedWithItsMeasurements()
    {
        var guide = Read("docs/connection-pooling.md");
        var future = Read("docs/FUTURE_WORK.md");
        var row = future.Split('\n').First(l => l.StartsWith("| SQLITE-008 |", StringComparison.Ordinal));

        Assert.Contains("Mixed_Pengdows_FairnessOff", row, StringComparison.Ordinal);
        Assert.Contains("Decided 2026-10-18", row, StringComparison.Ordinal);
        Assert.DoesNotContain("Open, maintainer decision", row, StringComparison.Ordinal);
        Assert.Contains("keep the default on", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("write latency", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EnableSingleWriterFairness = false", guide, StringComparison.Ordinal);
    }

    [Fact]
    public void PoolOneHundredOne_SaysTheInternalOnlyVariantWasDeclined()
    {
        var future = Read("docs/FUTURE_WORK.md");
        var row = future.Split('\n').First(l => l.StartsWith("| POOL-001 |", StringComparison.Ordinal));

        Assert.Contains("internal-only", row, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("benchmarks", row, StringComparison.OrdinalIgnoreCase);
    }

    // DuckDB.NET 1.5.6.1 (the release with our interval fixes) was validated with the workarounds in place; the finding is recorded.
    [Fact]
    public void DuckDbDriverCompatibility_IsRecordedWithTheVerifiedVersions()
    {
        var supported = Read("docs/supported-databases.md");
        var section = supported.Substring(supported.IndexOf("### DuckDB driver compatibility", StringComparison.Ordinal));

        Assert.Contains("1.5.6.1", section, StringComparison.Ordinal);
        Assert.Contains("1.5.6", section, StringComparison.Ordinal);
        Assert.Contains("TimeOnly", section, StringComparison.Ordinal);
        Assert.Contains("INTERVAL", section, StringComparison.Ordinal);
        Assert.Contains("BitArray", section, StringComparison.Ordinal);
    }
}
