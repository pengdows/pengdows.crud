// Quick validation of ValueTask allocation savings
using System;
using System.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.attributes;

[Table("test_entity")]
public class TestEntity
{
    [Id(false)]
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("name", DbType.String)]
    public string Name { get; set; } = "";

    [Column("value", DbType.Int32)]
    public int Value { get; set; }
}

class Program
{
    static async Task Main(string[] args)
    {
        using var context = new DatabaseContext(
            "Data Source=:memory:",
            SqliteFactory.Instance);

        // Setup
        using (var container = context.CreateSqlContainer())
        {
            container.Query.Append(@"
                CREATE TABLE test_entity (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT,
                    value INTEGER
                )");
            await container.ExecuteNonQueryAsync();
        }

        // Insert test data
        var helper = new TableGateway<TestEntity, int>(context);
        for (int i = 1; i <= 100; i++)
        {
            await helper.CreateAsync(new TestEntity { Name = $"Test{i}", Value = i }, context);
        }

        Console.WriteLine("ValueTask Migration Performance Test");
        Console.WriteLine("=====================================");
        Console.WriteLine();

        // Warm up
        for (int i = 0; i < 100; i++)
        {
            await helper.RetrieveOneAsync(1, context);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var memBefore = GC.GetTotalMemory(false);

        var sw = Stopwatch.StartNew();
        const int iterations = 10000;

        for (int i = 0; i < iterations; i++)
        {
            var entity = await helper.RetrieveOneAsync(1, context);
        }

        sw.Stop();

        var gen0After = GC.CollectionCount(0);
        var gen1After = GC.CollectionCount(1);
        var gen2After = GC.CollectionCount(2);
        var memAfter = GC.GetTotalMemory(false);

        Console.WriteLine($"Iterations: {iterations:N0}");
        Console.WriteLine($"Total Time: {sw.ElapsedMilliseconds:N0} ms");
        Console.WriteLine($"Per Operation: {(sw.ElapsedMilliseconds * 1000.0 / iterations):F2} μs");
        Console.WriteLine();
        Console.WriteLine("GC Collections:");
        Console.WriteLine($"  Gen 0: {gen0After - gen0Before}");
        Console.WriteLine($"  Gen 1: {gen1After - gen1Before}");
        Console.WriteLine($"  Gen 2: {gen2After - gen2Before}");
        Console.WriteLine();
        Console.WriteLine($"Memory Delta: {(memAfter - memBefore) / 1024:N0} KB");
        Console.WriteLine($"Per Operation: {(memAfter - memBefore) / iterations:N0} bytes");
        Console.WriteLine();
        Console.WriteLine("Expected with ValueTask:");
        Console.WriteLine("  - 30-50% fewer allocations per operation");
        Console.WriteLine("  - Fewer Gen 0 collections");
        Console.WriteLine("  - ~50-100 bytes saved per operation");
    }
}
