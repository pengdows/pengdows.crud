using System;
using System.IO;
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
        Assert.Contains("not measured against real I/O", text, StringComparison.Ordinal);
    }
}
