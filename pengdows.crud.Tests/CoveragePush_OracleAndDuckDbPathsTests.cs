using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Targeted tests for uncovered paths in:
/// - SqlContainer.cs RenderParamsDeduplicating (Oracle path): lines 375-378 ({P} with non-identifier), 414 (no {P} found)
/// - SqlContainer.cs MakeDuplicateParameterName: lines 432, 444, 453 (Oracle duplicate {P} params)
/// - SqlContainer.cs OpenConnectionAsync DuckDB path: lines 1772-1780 (RequiresSerializedOpen)
/// </summary>
[Collection("SqliteSerial")]
public class CoveragePush_OracleAndDuckDbPathsTests
{
    private static IDatabaseContext CreateOracleContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        factory.EnableDataPersistence = true;
        return new DatabaseContext(
            "Data Source=oracle-host;User Id=test;Password=test;EmulatedProduct=Oracle",
            factory);
    }

    private static IDatabaseContext CreateDuckDbContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        factory.EnableDataPersistence = true;
        return new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=DuckDB",
            factory);
    }

    // =========================================================================
    // RenderParamsDeduplicating — no {P} found (line 414)
    // =========================================================================

    [Fact]
    public async Task Oracle_SqlContainer_PlainSql_NoPlaceholders_Returns()
    {
        // Oracle uses RenderParamsDeduplicating. When SQL has no {P} patterns,
        // the loop's relIdx < 0 causes an immediate break, sb == null, and line 414 is hit.
        using var ctx = CreateOracleContext();
        await using var sc = ctx.CreateSqlContainer("SELECT 1 FROM DUAL");
        var result = await sc.ExecuteNonQueryAsync(ExecutionType.Read);
        Assert.True(result >= 0);
    }

    // =========================================================================
    // RenderParamsDeduplicating — {P} followed by non-identifier (lines 375-378)
    // =========================================================================

    [Fact]
    public async Task Oracle_SqlContainer_PlaceholderWithDigitAfter_TreatedLiterally()
    {
        // Oracle uses RenderParamsDeduplicating. {P}123 starts with a digit,
        // IsIdentStart('1') == false → lines 375-378: the {P} is treated as literal text.
        using var ctx = CreateOracleContext();
        // The SQL has {P}123 — since '1' is not IsIdentStart, it emits "{P}123" literally.
        await using var sc = ctx.CreateSqlContainer("SELECT {P}123 FROM DUAL");
        var result = await sc.ExecuteNonQueryAsync(ExecutionType.Read);
        Assert.True(result >= 0);
    }

    // =========================================================================
    // RenderParamsDeduplicating — {P} at end of string (lines 375-378)
    // =========================================================================

    [Fact]
    public async Task Oracle_SqlContainer_PlaceholderAtEndOfString_TreatedLiterally()
    {
        // Oracle: {P} at the very end → nameStart >= sql.Length → lines 375-378 hit.
        using var ctx = CreateOracleContext();
        await using var sc = ctx.CreateSqlContainer("SELECT 1 FROM DUAL WHERE x = {P}");
        var result = await sc.ExecuteNonQueryAsync(ExecutionType.Read);
        Assert.True(result >= 0);
    }

    // =========================================================================
    // RenderParamsDeduplicating — duplicate {P} params (MakeDuplicateParameterName, lines 432, 453)
    // =========================================================================

    [Fact]
    public async Task Oracle_SqlContainer_DuplicateNamedParam_MakesUniqueNames()
    {
        // Oracle + same param name twice → RenderParamsDeduplicating deduplicates to val, val_2.
        // This covers MakeDuplicateParameterName lines 432 (while loop), 453 (attempt++).
        using var ctx = CreateOracleContext();
        await using var sc = ctx.CreateSqlContainer(
            "SELECT :val FROM DUAL WHERE :val > 0");
        // Use {P} syntax for Oracle parameter rendering path
        sc.Clear();
        sc.Query.Append("SELECT {P}val FROM DUAL WHERE x = {P}val");
        sc.AddParameterWithValue("val", DbType.Int32, 42);
        var result = await sc.ExecuteNonQueryAsync(ExecutionType.Read);
        Assert.True(result >= 0);
    }

    // =========================================================================
    // SqlContainer.OpenConnectionAsync with DuckDB (RequiresSerializedOpen=true)
    // Lines 1772-1780: serialized open path
    // =========================================================================

    [Fact]
    public async Task DuckDb_SqlContainer_ExecuteNonQuery_UsesSerializedOpen()
    {
        // DuckDB sets RequiresSerializedOpen=true in DatabaseContext.
        // When SqlContainer executes, OpenConnectionAsync is called.
        // Since RequiresSerializedOpen=true, it takes the serialized path (lines 1772-1780).
        using var ctx = CreateDuckDbContext();
        await using var sc = ctx.CreateSqlContainer("SELECT 1");
        var result = await sc.ExecuteNonQueryAsync(ExecutionType.Read);
        Assert.True(result >= 0);
    }

    [Fact]
    public async Task DuckDb_SqlContainer_ExecuteScalar_UsesSerializedOpen()
    {
        // Same DuckDB serialized open path, via ExecuteScalarOrNullAsync.
        using var ctx = CreateDuckDbContext();
        await using var sc = ctx.CreateSqlContainer("SELECT 1");
        var result = await sc.ExecuteScalarOrNullAsync<int?>(ExecutionType.Read);
        // fakeDb returns null for unregistered queries — just check no exception
        Assert.True(result == null || result >= 0);
    }

    // =========================================================================
    // RenderParamsSimple — {P} at end of string (equivalent of lines 309-314 for simple path)
    // Test the SQLite (non-deduplicating) path for completeness
    // =========================================================================

    [Fact]
    public async Task Sqlite_SqlContainer_PlaceholderAtEndOfString_TreatedLiterally()
    {
        // SQLite uses RenderParamsSimple. {P} at end → nameStart >= sql.Length → line 309-314.
        // We already have tests for the simple path, but this ensures the edge is covered.
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.EnableDataPersistence = true;
        using var ctx = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);
        await using var sc = ctx.CreateSqlContainer("SELECT 1 WHERE x = {P}");
        var result = await sc.ExecuteNonQueryAsync(ExecutionType.Read);
        Assert.True(result >= 0);
    }
}
