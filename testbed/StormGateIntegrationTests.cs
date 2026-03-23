using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.stormgate;

namespace testbed;

public static class StormGateIntegrationTests
{
    public static async Task RunAsync()
    {
        Console.WriteLine("Running StormGate Integration Tests (SQLite)");

        // Use a file-based SQLite database for better concurrency testing than :memory:
        var dbPath = Path.Combine(Path.GetTempPath(), $"stormgate_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath};";

        try
        {
            await RunConcurrencyTest(connectionString, maxConcurrent: 5, totalTasks: 20);
            await RunTimeoutTest(connectionString);
            Console.WriteLine("StormGate Integration Tests: PASSED");
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    private static async Task RunConcurrencyTest(string connectionString, int maxConcurrent, int totalTasks)
    {
        Console.WriteLine($"  Testing concurrency (max={maxConcurrent}, tasks={totalTasks})");

        using var gate = StormGate.Create(
            SqliteFactory.Instance,
            connectionString,
            maxConcurrentOpens: maxConcurrent,
            acquireTimeout: TimeSpan.FromSeconds(5));

        var currentConcurrent = 0;
        var maxObserved = 0;
        var lockObj = new object();
        var completedTasks = 0;

        var tasks = Enumerable.Range(0, totalTasks).Select(async i =>
        {
            await using var conn = await gate.OpenAsync();

            int observed;
            lock (lockObj)
            {
                currentConcurrent++;
                maxObserved = Math.Max(maxObserved, currentConcurrent);
                observed = currentConcurrent;
            }

            // Simulate some work
            await Task.Delay(50);

            lock (lockObj)
            {
                currentConcurrent--;
                completedTasks++;
            }
        });

        await Task.WhenAll(tasks);

        Console.WriteLine($"    Max observed concurrency: {maxObserved}");

        if (maxObserved > maxConcurrent)
            throw new Exception($"Observed concurrency {maxObserved} exceeded limit {maxConcurrent}");

        if (completedTasks != totalTasks)
            throw new Exception($"Only {completedTasks}/{totalTasks} tasks completed");
    }

    private static async Task RunTimeoutTest(string connectionString)
    {
        Console.WriteLine("  Testing timeout/saturation");

        using var gate = StormGate.Create(
            SqliteFactory.Instance,
            connectionString,
            maxConcurrentOpens: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(100));

        // Take the only permit
        using var conn1 = await gate.OpenAsync();

        // Try to take another one, should timeout
        var sw = Stopwatch.StartNew();
        try
        {
            await gate.OpenAsync();
            throw new Exception("Should have timed out");
        }
        catch (TimeoutException)
        {
            sw.Stop();
            Console.WriteLine($"    Caught expected timeout after {sw.ElapsedMilliseconds}ms");
        }
    }
}
