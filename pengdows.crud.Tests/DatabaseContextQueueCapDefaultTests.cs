using System.Reflection;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Review 2026-09-29: 2.0.5 had no admission-queue cap (a waiting caller was bounded only by
/// PoolAcquireTimeout). 2.0.6 added a default cap (max(8 x slots, 32)), so a burst that 2.0.5 served
/// by waiting now fails immediately with PoolSaturatedException, a behavior change in a patch
/// release. On 2.0.x the cap stays opt-in (MaxQueuedReads/MaxQueuedWrites); 3.0 makes it the default.
/// </summary>
public sealed class DatabaseContextQueueCapDefaultTests
{
    private static PoolGovernor Governor(DatabaseContext context, string field) =>
        (PoolGovernor)typeof(DatabaseContext).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(context)!;

    [Fact]
    public void DefaultConfiguration_HasNoQueueCap_As2_0_5()
    {
        using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=q;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard
        }, new fakeDbFactory(SupportedDatabase.SqlServer));

        Assert.Equal(int.MaxValue, Governor(context, "_writerGovernor").MaxQueueDepth);
        Assert.Equal(int.MaxValue, Governor(context, "_readerGovernor").MaxQueueDepth);
    }

    [Fact]
    public void ExplicitQueueCaps_AreApplied()
    {
        using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=q;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            MaxQueuedWrites = 5,
            MaxQueuedReads = 7
        }, new fakeDbFactory(SupportedDatabase.SqlServer));

        Assert.Equal(5, Governor(context, "_writerGovernor").MaxQueueDepth);
        Assert.Equal(7, Governor(context, "_readerGovernor").MaxQueueDepth);
    }
}
