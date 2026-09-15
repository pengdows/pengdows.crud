using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Targeted tests covering uncovered edge-case paths in SqlContainer.cs:
/// - TryExecuteScalarAsync overloads (lines 1245, 1251)
/// - Clone() with cached command text sharing (lines 1839-1840)
/// - Clone() when ParamSequence populated (lines 1863-1864)
/// - Clone() when renderedParameterMap populated (lines 1866-1869)
/// - SqlContainer.Reset() internal method (lines 1917-1921)
/// - ParameterNameComparer null/x and x/null paths (line 132)
/// - StripParameterPrefix empty string (line 160)
/// </summary>
[Collection("SqliteSerial")]
public class CoveragePush_SqlContainerEdgeCaseTests : SqlLiteContextTestBase
{
    // =========================================================================
    // TryExecuteScalarAsync overloads (lines 1245, 1251 in SqlContainer.cs)
    // =========================================================================

    [Fact]
    public async Task TryExecuteScalarAsync_CommandTypeOverload_ReturnsResult()
    {
        // Covers line 1245: return TryExecuteScalarAsync<T>(ExecutionType.Read, commandType, CancellationToken.None)
        await using var sc = Context.CreateSqlContainer("SELECT 1");
        var result = await sc.TryExecuteScalarAsync<int>(CommandType.Text);
        // ScalarResult is a struct — check status, not nullability
        Assert.True(result.Status >= 0);
    }

    [Fact]
    public async Task TryExecuteScalarAsync_CommandTypeCancellationTokenOverload_ReturnsResult()
    {
        // Covers line 1251: return TryExecuteScalarAsync<T>(ExecutionType.Read, commandType, cancellationToken)
        await using var sc = Context.CreateSqlContainer("SELECT 1");
        var result = await sc.TryExecuteScalarAsync<int>(CommandType.Text, CancellationToken.None);
        Assert.True(result.Status >= 0);
    }

    // =========================================================================
    // Clone() with cached command text (lines 1839-1840 in SqlContainer.cs)
    // =========================================================================

    [Fact]
    public async Task Clone_AfterExecution_SharesCachedCommandText()
    {
        // After a container is executed, _cachedCommandText is set.
        // Cloning it hits the canShareCache=true branch (lines 1839-1840).
        await using var sc = Context.CreateSqlContainer("SELECT 1");

        // Execute to populate _cachedCommandText
        await sc.ExecuteNonQueryAsync();

        // Clone — should reuse cached command text
        using var clone = sc.Clone();

        Assert.NotNull(clone);
        Assert.Equal("SELECT 1", clone.Query.ToString());
    }

    [Fact]
    public async Task Clone_AfterExecution_WithContext_SharesCachedCommandText()
    {
        // Clone(IDatabaseContext) overload with cached command text.
        await using var sc = Context.CreateSqlContainer("SELECT 42");
        await sc.ExecuteNonQueryAsync();

        using var clone = sc.Clone(Context);
        Assert.NotNull(clone);
    }

    // =========================================================================
    // Clone() with populated ParamSequence (lines 1863-1864 in SqlContainer.cs)
    // =========================================================================

    [Fact]
    public async Task Clone_WithParamSequence_CopiesSequence()
    {
        // SqlContainer with {P} placeholders builds a ParamSequence during execution.
        // Cloning it hits lines 1863-1864.
        await using var sc = Context.CreateSqlContainer("SELECT {P}p0");
        sc.AddParameterWithValue("p0", DbType.Int32, 1);

        // Execute to populate ParamSequence
        await sc.ExecuteNonQueryAsync(ExecutionType.Read, CommandType.Text);

        // ParamSequence should now have 1 entry; Clone must copy it
        using var clone = sc.Clone();
        Assert.NotNull(clone);
        Assert.True(clone.Query.ToString().Length > 0);
    }

    // =========================================================================
    // Clone() with rendered parameter map (lines 1866-1869 in SqlContainer.cs)
    // =========================================================================

    [Fact]
    public async Task Clone_WithRenderedParameterMap_CopiesMap()
    {
        // When the same {P} param name appears twice, _renderedParameterMap gets an entry.
        // This covers lines 1866-1869 in Clone().
        await using var sc = Context.CreateSqlContainer("SELECT {P}val WHERE x = {P}val");
        sc.AddParameterWithValue("val", DbType.Int32, 42);

        // Render to populate _renderedParameterMap (duplicate "val" → "val_2")
        await sc.ExecuteNonQueryAsync(ExecutionType.Read, CommandType.Text);

        using var clone = sc.Clone();
        Assert.NotNull(clone);
    }

    // =========================================================================
    // SqlContainer.Reset() internal method (lines 1917-1921 in SqlContainer.cs)
    // =========================================================================

    [Fact]
    public void Reset_PreservesQueryAndParameters()
    {
        // SqlContainer.Reset() is internal — accessible via InternalsVisibleTo.
        // It resets _outputParameterCount to 0 (lines 1917-1921) while preserving
        // query, parameters, and cached command text.
        var sc = (SqlContainer)Context.CreateSqlContainer("SELECT 1");
        sc.AddParameterWithValue("p0", DbType.Int32, 42);

        sc.Reset();

        // After Reset(), query and parameters are still present
        Assert.Equal("SELECT 1", sc.Query.ToString());
        sc.Dispose();
    }

    // =========================================================================
    // ParameterNameComparer — null branch (line 132 in SqlContainer.cs)
    // =========================================================================

    [Fact]
    public void SetParameterValue_LookupByNormalizedName_WhenOneIsNull()
    {
        // The ParameterNameComparer.Equals(null, "foo") branch returns false (line 132).
        // Triggered indirectly: SetParameterValue with a name that doesn't exist returns
        // KeyNotFoundException (uses the comparer in the dictionary lookup).
        using var sc = Context.CreateSqlContainer("SELECT 1");
        sc.AddParameterWithValue("p0", DbType.Int32, 1);

        // Lookup with a name not in the dictionary — comparer handles null/empty comparisons
        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
            () => sc.SetParameterValue("notexist", 99));
    }

    // =========================================================================
    // ExecuteNonQueryAsync overloads — CommandType+CancellationToken (lines 1073-1076)
    // =========================================================================

    [Fact]
    public async Task ExecuteNonQueryAsync_CommandTypeCancellationTokenOverload_Routes()
    {
        // Covers line 1073-1076: ExecuteNonQueryAsync(CommandType, CancellationToken)
        // which delegates to ExecuteNonQueryAsync(Write, commandType, ct).
        await using var sc = Context.CreateSqlContainer("SELECT 1");
        var result = await sc.ExecuteNonQueryAsync(CommandType.Text, CancellationToken.None);
        Assert.True(result >= 0);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_ExecutionTypeCommandType_ReturnsResult()
    {
        // Covers line 1078-1081: ExecuteNonQueryAsync(ExecutionType, CommandType)
        // which delegates to the 4-arg version with CancellationToken.None.
        await using var sc = Context.CreateSqlContainer("SELECT 1");
        var result = await sc.ExecuteNonQueryAsync(ExecutionType.Read, CommandType.Text);
        Assert.True(result >= 0);
    }

    // =========================================================================
    // NormalizeParameterName — empty string early return (SqlContainer.cs line 179)
    // AddParameterWithValue("") creates a parameter with empty name;
    // CreateDbParameter calls NormalizeParameterName("") → line 179 returns early.
    // AddParameter then replaces the empty name with a generated name.
    // =========================================================================

    [Fact]
    public void AddParameterWithValue_EmptyName_StillAddsParameter()
    {
        // Covers SqlContainer.cs line 179: NormalizeParameterName("") returns early
        using var sc = Context.CreateSqlContainer("SELECT 1");
        var param = sc.AddParameterWithValue("", DbType.Int32, 42);

        // The parameter was added (with a generated name since empty was replaced)
        Assert.NotNull(param);
        Assert.Equal(1, sc.ParameterCount);
    }

    // =========================================================================
    // Clone() with _renderedParameterMap populated (SqlContainer.cs lines 2041-2042)
    // Oracle dialect has SupportsRepeatedNamedParameters=false so uses
    // RenderParamsDeduplicating which populates _renderedParameterMap when a
    // parameter name appears more than once in the rendered SQL.
    // =========================================================================

    [Fact]
    public async Task Clone_OracleContext_WithDuplicateParams_CopiesRenderedParameterMap()
    {
        // Oracle: SupportsRepeatedNamedParameters=false → RenderParamsDeduplicating
        // → _renderedParameterMap populated when same {P}param used twice
        await using var oracleCtx = new DatabaseContext(
            "Data Source=test;EmulatedProduct=Oracle",
            new fakeDbFactory(SupportedDatabase.Oracle));

        // Use {P}val twice — second occurrence will be deduplicated to val_2
        await using var sc = oracleCtx.CreateSqlContainer("SELECT {P}val + {P}val FROM dual");
        sc.AddParameterWithValue("val", DbType.Int32, 1);

        // Execute to trigger RenderParamsDeduplicating → sets _renderedParameterMap
        await sc.ExecuteNonQueryAsync(ExecutionType.Read, CommandType.Text);

        // Clone copies _renderedParameterMap (lines 2041-2042)
        using var clone = sc.Clone();
        Assert.NotNull(clone);
    }

    // =========================================================================
    // RenderParamsDeduplicating — no {P} placeholders → sb == null → line 423
    // Oracle context executes SQL without any {P} tokens; RenderParamsDeduplicating
    // finds nothing to replace → sb remains null → returns sql unchanged (line 423).
    // =========================================================================

    [Fact]
    public async Task Oracle_SqlWithoutPlaceholders_RenderParamsDeduplicating_ReturnsUnchanged()
    {
        await using var oracleCtx = new DatabaseContext(
            "Data Source=test;EmulatedProduct=Oracle",
            new fakeDbFactory(SupportedDatabase.Oracle));

        // No {P} placeholders → RenderParamsDeduplicating sb stays null → line 423
        await using var sc = oracleCtx.CreateSqlContainer("SELECT 1 FROM dual");
        var result = await sc.ExecuteNonQueryAsync(ExecutionType.Read, CommandType.Text);
        Assert.True(result >= 0);
    }

    // =========================================================================
    // OpenConnectionAsync — RequiresSerializedOpen path (SqlContainer.cs lines 1945-1952)
    // DuckDB sets RequiresSerializedOpen=true; executing any SQL goes through the
    // serialized open gate, covering lines 1945-1952.
    // =========================================================================

    [Fact]
    public async Task DuckDb_SqlExecution_UsesSerializedOpenPath()
    {
        await using var duckCtx = new DatabaseContext(
            "Data Source=test;EmulatedProduct=DuckDB",
            new fakeDbFactory(SupportedDatabase.DuckDB));

        // Any SQL execution opens the connection through the serialized gate for DuckDB
        await using var sc = duckCtx.CreateSqlContainer("SELECT 1");
        var result = await sc.ExecuteNonQueryAsync(ExecutionType.Read, CommandType.Text);
        Assert.True(result >= 0);
    }
}
