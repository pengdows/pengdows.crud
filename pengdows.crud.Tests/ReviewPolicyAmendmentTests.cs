using System;
using System.IO;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The review-policy amendments the external audit of 2.0.6 asked for (P1, P2, P4, P6), pinned so the policy keeps saying them.
/// </summary>
public sealed class ReviewPolicyAmendmentTests
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

    private static string Policy() => File.ReadAllText(Path.Combine(Root(), "REVIEW_POLICY.md"));

    [Fact]
    public void P1_UpsertChangesRequireAConcurrentSameKeyTest()
    {
        var text = Policy();
        Assert.Contains("concurrent same-key test", text, StringComparison.Ordinal);
        Assert.Contains("upsert", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void P2_SkipsAreCapabilitySkipsOnly()
    {
        var text = Policy();
        Assert.DoesNotContain("* No skipped tests.", text, StringComparison.Ordinal);
        Assert.Contains("capability skips only", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[SKIP:<Product>]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void P4_EmptyCatchBlocksNeedAStatedReason()
    {
        var text = Policy();
        Assert.Contains("empty catch", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stated reason", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void P6_MockingFrameworksAreBannedAndEnforced()
    {
        var text = Policy();
        Assert.Contains("No mocking framework", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NoMockingFrameworkTests", text, StringComparison.Ordinal);
    }
}
