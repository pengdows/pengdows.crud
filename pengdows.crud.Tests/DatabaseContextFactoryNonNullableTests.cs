using System;
using System.Data.Common;
using System.Reflection;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Verifies that the <c>_factory</c> field in DatabaseContext is declared as
/// non-nullable (<c>DbProviderFactory</c>) rather than <c>DbProviderFactory?</c>.
///
/// The field is always assigned via <c>factory ?? throw ArgumentNullException</c>
/// in the canonical constructor, so it is never null post-construction.
/// Declaring it nullable causes unnecessary null-forgiving operators (<c>!</c>)
/// throughout the codebase and suppresses legitimate nullability warnings.
///
/// TDD contract:
///   - _FieldIsNonNullable FAILS while the field is still declared nullable (red).
///   - It PASSES once the declaration is corrected (green).
///   - The remaining tests verify the null-guard contract and code-path coverage
///     so we can confidently make the type change without regression.
/// </summary>
public class DatabaseContextFactoryNonNullableTests
{
    private static readonly BindingFlags AnyInstance =
        BindingFlags.NonPublic | BindingFlags.Instance;

    // -------------------------------------------------------------------------
    // Red-phase: field declaration must be non-nullable
    // -------------------------------------------------------------------------

    [Fact]
    public void Factory_Field_IsDeclaredNonNullable()
    {
        var field = typeof(DatabaseContext).GetField("_factory", AnyInstance);
        Assert.NotNull(field);

        // A nullable reference type is represented at runtime as the plain type
        // (nullability is a compile-time annotation tracked via NullableAttribute).
        // We verify that the field type itself is DbProviderFactory — not wrapped —
        // AND that no NullableAttribute marks it as nullable(2) on the field.
        Assert.Equal(typeof(DbProviderFactory), field!.FieldType);

        var nullableAttr = field.GetCustomAttribute<System.Runtime.CompilerServices.NullableAttribute>();
        // NullableAttribute(1) = non-nullable, NullableAttribute(2) = nullable.
        // If the attribute is absent the context default applies (non-nullable in our case).
        if (nullableAttr != null)
        {
            Assert.Equal((byte)1, nullableAttr.NullableFlags[0]);
        }
    }

    // -------------------------------------------------------------------------
    // Contract: constructor null-guard is preserved after the type change
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_NullFactory_ThrowsArgumentNullException()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite"
        };

        Assert.Throws<ArgumentNullException>(
            () => new DatabaseContext(config, null!));
    }

    // -------------------------------------------------------------------------
    // Coverage: exercise every code path that previously used _factory!
    // These must all pass before and after the declaration change.
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidFactory_CreateSqlContainer_Works()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=Sqlite", factory);

        using var sc = ctx.CreateSqlContainer("SELECT 1");
        Assert.NotNull(sc);
    }

    [Fact]
    public void ValidFactory_GetConnection_Works()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=Sqlite", factory);

        var conn = ctx.GetConnection(ExecutionType.Read);
        Assert.NotNull(conn);
        ctx.CloseAndDisposeConnection(conn);
    }

    [Fact]
    public void ValidFactory_DataSourceInfo_IsPopulated()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=Sqlite", factory);

        Assert.NotNull(ctx.DataSourceInfo);
    }

    [Fact]
    public void ValidFactory_WrapObjectName_Works()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=Sqlite", factory);

        var wrapped = ctx.WrapObjectName("MyTable");
        Assert.False(string.IsNullOrEmpty(wrapped));
    }
}
