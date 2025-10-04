using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using pengdows.crud;
using testbed.Cockroach;

namespace testbed;

public class ParallelTestOrchestrator
{
    private readonly IServiceProvider _services;
    private readonly ConcurrentBag<TestResult> _results = new();
    private readonly bool _includeOracle;

    public ParallelTestOrchestrator(IServiceProvider services, bool includeOracle = false)
    {
        _services = services;
        _includeOracle = includeOracle;
    }

    public async Task<IReadOnlyCollection<TestResult>> RunAllTestsAsync(
        ISet<string>? only = null,
        ISet<string>? exclude = null)
    {
        var testConfigurations = GetTestConfigurations();
        
        // Apply filtering if provided
        if (only is { Count: > 0 })
        {
            testConfigurations = testConfigurations
                .Where(c => only.Contains(c.ContainerName, StringComparer.OrdinalIgnoreCase) ||
                            only.Contains(c.DatabaseProvider, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        if (exclude is { Count: > 0 })
        {
            testConfigurations = testConfigurations
                .Where(c => !exclude.Contains(c.ContainerName, StringComparer.OrdinalIgnoreCase) &&
                            !exclude.Contains(c.DatabaseProvider, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        Console.WriteLine($"Starting {testConfigurations.Count} test containers in parallel...");
        
        // Start all containers in parallel
        var testTasks = testConfigurations.Select(config => RunTestAsync(config)).ToArray();
        
        // Wait for all tests to complete
        await Task.WhenAll(testTasks);
        
        // Display summary
        DisplayResults();
        
        return _results.ToArray();
    }

    private async Task RunTestAsync(TestConfiguration config)
    {
        var startTime = DateTime.UtcNow;
        TestResult result = new()
        {
            ContainerName = config.ContainerName,
            DatabaseProvider = config.DatabaseProvider,
            StartTime = startTime
        };

        try
        {
            Console.WriteLine($"[{config.ContainerName}] Starting container...");
            
            // Start container (this may take time for Docker containers)
            await config.Container.StartAsync();
            
            var containerStarted = DateTime.UtcNow;
            result.ContainerStartTime = containerStarted - startTime;
            
            Console.WriteLine($"[{config.ContainerName}] Container ready in {result.ContainerStartTime.TotalSeconds:F2}s, starting tests...");
            
            // Get database context and run tests
            var dbContext = await config.Container.GetDatabaseContextAsync(_services);
            var testProvider = config.TestProviderFactory(dbContext, _services);
            
            await testProvider.RunTest();
            
            result.Success = true;
            result.TotalTime = DateTime.UtcNow - startTime;
            result.TestTime = result.TotalTime - result.ContainerStartTime;
            
            Console.WriteLine($"[{config.ContainerName}] ✅ Tests completed in {result.TestTime?.TotalSeconds:F2}s (total: {result.TotalTime.TotalSeconds:F2}s)");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.TotalTime = DateTime.UtcNow - startTime;
            
            Console.WriteLine($"[{config.ContainerName}] ❌ Failed: {ex.Message}");
        }
        finally
        {
            try
            {
                var keep = string.Equals(Environment.GetEnvironmentVariable("TESTBED_KEEP_CONTAINERS"), "true", StringComparison.OrdinalIgnoreCase);
                if (!keep)
                {
                    await config.Container.DisposeAsync();
                    Console.WriteLine($"[{config.ContainerName}] Container disposed");
                }
                else
                {
                    Console.WriteLine($"[{config.ContainerName}] Keeping container running (TESTBED_KEEP_CONTAINERS=true)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{config.ContainerName}] Warning: Failed to dispose container: {ex.Message}");
            }
        }

        _results.Add(result);
    }

    private List<TestConfiguration> GetTestConfigurations()
    {
        var configurations = new List<TestConfiguration>
        {
            new()
            {
                ContainerName = "SQLite",
                DatabaseProvider = "SQLite",
                Container = new SqliteTestContainer(), // We'll need to create this
                TestProviderFactory = (db, sp) => new TestProvider(db, sp)
            },
            new()
            {
                ContainerName = "DuckDB", 
                DatabaseProvider = "DuckDB",
                Container = new DuckDbTestContainer(),
                TestProviderFactory = (db, sp) => new TestProvider(db, sp)
            },
            new()
            {
                ContainerName = "PostgreSQL",
                DatabaseProvider = "PostgreSQL", 
                Container = new PostgreSqlTestContainer(),
                TestProviderFactory = (db, sp) => new PostgreSQLTestProvider(db, sp)
            },
            new()
            {
                ContainerName = "MySQL",
                DatabaseProvider = "MySQL",
                Container = new MySqlTestContainer(), 
                TestProviderFactory = (db, sp) => new TestProvider(db, sp)
            },
            new()
            {
                ContainerName = "MariaDB",
                DatabaseProvider = "MariaDB",
                Container = new MariaDbContainer(),
                TestProviderFactory = (db, sp) => new TestProvider(db, sp)
            },
            new()
            {
                ContainerName = "SQL Server",
                DatabaseProvider = "SQL Server", 
                Container = new SqlServerTestContainer(),
                TestProviderFactory = (db, sp) => new TestProvider(db, sp)
            },
            new()
            {
                ContainerName = "CockroachDB",
                DatabaseProvider = "CockroachDB",
                Container = new CockroachDbTestContainer(),
                TestProviderFactory = (db, sp) => new CockroachDbTestProvider(db, sp)
            },
            new()
            {
                ContainerName = "Firebird", 
                DatabaseProvider = "Firebird",
                Container = new FirebirdSqlTestContainer(),
                TestProviderFactory = (db, sp) => new FirebirdTestProvider(db, sp)
            },
            // Add Sybase as needed
        };

        // Oracle - check if external Oracle is available
        if (_includeOracle)
        {
            configurations.Add(new()
            {
                ContainerName = "Oracle",
                DatabaseProvider = "Oracle", 
                Container = new ExternalOracleTestContainer(), // Use external Oracle instead of managing container
                TestProviderFactory = (db, sp) => new OracleTestProvider(db, sp)
            });
        }

        // Additional databases can be added here:
        // - DB2 (ibmcom/db2) - requires IBM.Data.DB2 package
        // - Sybase ASE - requires AdoNetCore.AseClient (already available)
        // - Others as needed

        return configurations;
    }

    private void DisplayResults()
    {
        Console.WriteLine("\n" + "=".PadRight(80, '='));
        Console.WriteLine("TEST RESULTS SUMMARY");
        Console.WriteLine("=".PadRight(80, '='));

        var results = _results.OrderBy(r => r.ContainerName).ToArray();
        var successful = results.Count(r => r.Success);
        var failed = results.Length - successful;

    Console.WriteLine($"Total: {results.Length} | Successful: {successful} | Failed: {failed}");
        Console.WriteLine();

        // Results table
        Console.WriteLine($"{"Container",-15} {"Provider",-12} {"Status",-8} {"Start",-8} {"Test",-8} {"Total",-8} {"Error",-30}");
        Console.WriteLine(new string('-', 80));

        foreach (var result in results)
        {
            var status = result.Success ? "✅ PASS" : "❌ FAIL";
            var startTime = result.ContainerStartTime.TotalSeconds.ToString("F2") + "s";
            var testTime = result.TestTime.HasValue ? result.TestTime.Value.TotalSeconds.ToString("F2") + "s" : "N/A";
            var totalTime = result.TotalTime.TotalSeconds.ToString("F2") + "s";
            var error = result.Error?.Substring(0, Math.Min(result.Error.Length, 30)) ?? "";

            Console.WriteLine($"{result.ContainerName,-15} {result.DatabaseProvider,-12} {status,-8} {startTime,-8} {testTime,-8} {totalTime,-8} {error,-30}");
        }

        Console.WriteLine();

        if (failed > 0)
        {
            Console.WriteLine("FAILURES:");
            foreach (var failure in results.Where(r => !r.Success))
            {
                Console.WriteLine($"  {failure.ContainerName}: {failure.Error}");
            }
        }

        var maxTime = results.Max(r => r.TotalTime.TotalSeconds);
        var avgTime = results.Average(r => r.TotalTime.TotalSeconds);
        Console.WriteLine($"Execution completed in {maxTime:F2}s (avg: {avgTime:F2}s per container)");
        Console.WriteLine("=".PadRight(80, '='));
    }
}

public class TestConfiguration
{
    public required string ContainerName { get; set; }
    public required string DatabaseProvider { get; set; }
    public required ITestContainer Container { get; set; }
    public required Func<IDatabaseContext, IServiceProvider, TestProvider> TestProviderFactory { get; set; }
}

public class TestResult
{
    public required string ContainerName { get; set; }
    public required string DatabaseProvider { get; set; }
    public required DateTime StartTime { get; set; }
    public TimeSpan ContainerStartTime { get; set; }
    public TimeSpan? TestTime { get; set; }
    public TimeSpan TotalTime { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}
