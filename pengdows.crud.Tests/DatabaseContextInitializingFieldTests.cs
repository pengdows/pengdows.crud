using System;
using System.Reflection;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Verifies that the dead <c>_initializing</c> field has been removed from
/// DatabaseContext. The field was written by Interlocked.Exchange during
/// construction but never read anywhere, making it unreachable dead code.
///
/// TDD contract:
///   - This test FAILS while <c>_initializing</c> still exists (red).
///   - It PASSES once the field is deleted (green).
/// </summary>
public class DatabaseContextInitializingFieldTests
{
    [Fact]
    public void DatabaseContext_DoesNotContain_InitializingField()
    {
        var field = typeof(DatabaseContext).GetField(
            "_initializing",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.Null(field);
    }

    /// <summary>
    /// Construction must still complete fully and produce a usable context after
    /// the field is removed — guards against accidental breakage during cleanup.
    /// </summary>
    [Fact]
    public void DatabaseContext_AfterConstruction_IsFullyUsable()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=Sqlite", factory);

        // Key post-construction invariants that _initializing was presumed to guard
        Assert.NotNull(ctx);
        Assert.Equal(DbMode.SingleConnection, ctx.ConnectionMode); // :memory: auto-selects
        Assert.NotNull(ctx.CreateSqlContainer("SELECT 1"));
    }

    /// <summary>
    /// Verifies the two Interlocked.Exchange call-sites are also gone — they only
    /// existed to set the deleted field.
    /// </summary>
    [Fact]
    public void DatabaseContext_InitializationPartialClass_DoesNotReferenceInitializingField()
    {
        // If _initializing is gone, no field with that name should exist anywhere
        // on the type hierarchy either.
        var allFields = typeof(DatabaseContext)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

        Assert.DoesNotContain(allFields, f => f.Name == "_initializing");
    }
}
