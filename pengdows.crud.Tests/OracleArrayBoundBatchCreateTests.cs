using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// FEAT-005: TableGateway<TEntity,TRowID>.BuildBatchCreate/BatchCreateAsync choose Oracle's
// array-binding execution strategy (OracleDialect.SupportsArrayBinding) instead of the multi-row
// "INSERT ALL ... SELECT 1 FROM DUAL" shape, when available. See
// docs/planning/bulk-loading-design.md's Part 2. Purely an internal execution-strategy swap — the
// public BuildBatchCreate/BatchCreateAsync contract is unchanged, matching every other batch path.
public sealed class OracleArrayBoundBatchCreateTests
{
    [Table("array_bind_probe")]
    private sealed class ArrayBoundEntity
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string? Name { get; set; }
        [Column("amount", DbType.Decimal)] public decimal Amount { get; set; }
    }

    // Property discovered via reflection; class name contains "Oracle" so
    // GetType().FullName?.Contains("Oracle") matches OracleDialect.ConfigureArrayBinding's check.
    private sealed class FakeOracleCommand : fakeDbCommand
    {
        public FakeOracleCommand(System.Data.Common.DbConnection connection) : base(connection)
        {
        }

        public int ArrayBindCount { get; set; } = -1;
    }

    private static (fakeDbFactory Factory, DatabaseContext Context, TableGateway<ArrayBoundEntity, int> Gateway) CreateOracleGateway()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle)
        {
            CommandFactory = c => new FakeOracleCommand(c)
        };
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Oracle", factory);
        var gateway = new TableGateway<ArrayBoundEntity, int>(context);
        return (factory, context, gateway);
    }

    [Fact]
    public void BuildBatchCreate_ForOracle_ProducesSingleRowShapedInsert_NotInsertAll()
    {
        var (_, _, gateway) = CreateOracleGateway();
        var entities = new[]
        {
            new ArrayBoundEntity { Id = 1, Name = "Alice", Amount = 10m },
            new ArrayBoundEntity { Id = 2, Name = "Bob", Amount = 20m },
            new ArrayBoundEntity { Id = 3, Name = "Charlie", Amount = 30m }
        };

        var containers = gateway.BuildBatchCreate(entities);

        var sc = Assert.Single(containers);
        var sql = sc.Query.ToString();
        Assert.DoesNotContain("INSERT ALL", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO", sql, StringComparison.Ordinal);
        // One VALUES clause, not one per row.
        Assert.Equal(1, sql.Split("VALUES", StringSplitOptions.None).Length - 1);
        // One parameter per column (3), not one per cell (3 rows x 3 columns = 9).
        Assert.Equal(3, sc.ParameterCount);
    }

    [Fact]
    public async Task BatchCreateAsync_ForOracle_ConfiguresArrayBindCount_AndBindsColumnArraysWithNulls()
    {
        var (factory, _, gateway) = CreateOracleGateway();
        var entities = new[]
        {
            new ArrayBoundEntity { Id = 1, Name = "Alice", Amount = 10m },
            new ArrayBoundEntity { Id = 2, Name = null, Amount = 20m }, // NULL in the middle
            new ArrayBoundEntity { Id = 3, Name = "Charlie", Amount = 30m }
        };

        var affected = await gateway.BatchCreateAsync(entities);

        Assert.True(affected >= 0); // fakeDb's default non-query result; real correctness is proven below
        var conn = factory.CreatedConnections.Last();
        // The connection also runs an ALTER SESSION session-settings command before any real work;
        // find the INSERT specifically rather than assuming it's the only non-query executed.
        var captured = conn.ExecutedNonQueryCommands.Single(c => c.CommandText.StartsWith("INSERT", StringComparison.Ordinal));

        // One parameter per column, each bound to a 3-element array — not 9 scalar parameters.
        Assert.Equal(3, captured.Parameters.Count);
        foreach (var parameter in captured.Parameters)
        {
            var array = Assert.IsType<object[]>(parameter.Value);
            Assert.Equal(3, array.Length);
        }

        var nameArray = captured.Parameters
            .Select(p => (object[])p.Value!)
            .Single(a => a[0] is string);
        Assert.Equal("Alice", nameArray[0]);
        Assert.Equal(DBNull.Value, nameArray[1]); // NULL represented in-array, not inlined as a literal
        Assert.Equal("Charlie", nameArray[2]);

        var lastCommand = (FakeOracleCommand)conn.LastCreatedCommand!;
        Assert.Equal(3, lastCommand.ArrayBindCount);
    }

    [Fact]
    public async Task BatchCreateAsync_ForOracle_SingleChunk_MatchesBuildBatchCreateShape()
    {
        var (factory, _, gateway) = CreateOracleGateway();
        var entities = Enumerable.Range(1, 5)
            .Select(i => new ArrayBoundEntity { Id = i, Name = $"Row{i}", Amount = i })
            .ToArray();

        await gateway.BatchCreateAsync(entities);

        var conn = factory.CreatedConnections.Last();
        // Exactly one INSERT executed for the whole 5-row batch, proving it wasn't chunked into
        // per-entity fallback containers (the session-settings ALTER SESSION command is the other
        // entry in this connection's non-query history).
        Assert.Single(conn.ExecutedNonQueryCommands, c => c.CommandText.StartsWith("INSERT", StringComparison.Ordinal));
    }
}
