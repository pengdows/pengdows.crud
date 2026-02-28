using System.Collections.Concurrent;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using testbed.Cockroach;
using testbed.DuckDb;
using testbed.Firebird;
using testbed.mariaDb;
using testbed.MySQL;
using testbed.Oracle;
using testbed.PostgreSQL;
using testbed.SqlServer;
using testbed.TiDB;
using testbed.Snowflake;
using testbed.Yugabyte;

namespace testbed;

public class ParallelTestOrchestrator
{
    private readonly IServiceProvider _services;
    private readonly ConcurrentBag<TestResult> _results = new();
    private readonly bool _includeOracle;
    private readonly bool _includeSnowflake;
    private readonly bool _includeYugabyte;

    public ParallelTestOrchestrator(IServiceProvider services, bool includeOracle = false,
        bool includeSnowflake = false, bool includeYugabyte = false)
    {
        _services = services;
        _includeOracle = includeOracle;
        _includeSnowflake = includeSnowflake;
        _includeYugabyte = includeYugabyte;
    }

    /// <summary>
    /// Create and start a test container for a specific database provider.
    /// Used by integration tests that need individual containers.
    /// </summary>
    public async Task<ITestContainer?> CreateContainerAsync(SupportedDatabase provider)
    {
        ITestContainer? container = provider switch
        {
            SupportedDatabase.Sqlite => new SqliteTestContainer(),
            SupportedDatabase.PostgreSql => new PostgreSqlTestContainer(),
            SupportedDatabase.SqlServer => new SqlServerTestContainer(),
            SupportedDatabase.MySql => new MySqlTestContainer(),
            SupportedDatabase.MariaDb => new MariaDbContainer(),
            SupportedDatabase.Oracle when _includeOracle => new OracleTestContainer(),
            SupportedDatabase.Firebird => new FirebirdSqlTestContainer(),
            SupportedDatabase.CockroachDb => new CockroachDbTestContainer(),
            SupportedDatabase.DuckDB => new DuckDbTestContainer(),
            SupportedDatabase.YugabyteDb when _includeYugabyte => new YugabyteTestContainer(),
            SupportedDatabase.TiDb => new TiDBTestContainer(),
            SupportedDatabase.Snowflake when _includeSnowflake => new SnowflakeTestContainer(),
            _ => null
        };

        if (container != null)
        {
            await container.StartAsync();
        }

        return container;
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

        Console.WriteLine($"Starting {testConfigurations.Count} test containers (max 2 parallel)...");

        // Limit parallelism to 2 to prevent host saturation
        var semaphore = new SemaphoreSlim(2);
        var testTasks = testConfigurations.Select(async config =>
        {
            await semaphore.WaitAsync();
            try
            {
                await RunTestAsync(config);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

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

            var containerSw = System.Diagnostics.Stopwatch.StartNew();
            await config.Container.StartAsync();
            containerSw.Stop();
            result.ContainerStartTime = containerSw.Elapsed;

            Console.WriteLine(
                $"[{config.ContainerName}] Container ready in {result.ContainerStartTime.TotalSeconds:F2}s, starting tests...");

            var dbContext = await config.Container.GetDatabaseContextAsync(_services);
            var testProvider = config.TestProviderFactory(dbContext, _services);

            var testSw = System.Diagnostics.Stopwatch.StartNew();
            await testProvider.RunTest();
            testSw.Stop();

            result.Success = true;
            result.TestTime = testSw.Elapsed;
            result.TotalTime = DateTime.UtcNow - startTime;

            Console.WriteLine(
                $"[{config.ContainerName}] ✅ Tests completed in {result.TestTime.Value.TotalSeconds:F2}s");
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
                var keep = string.Equals(Environment.GetEnvironmentVariable("TESTBED_KEEP_CONTAINERS"), "true",
                    StringComparison.OrdinalIgnoreCase);
                if (!keep)
                {
                    await config.Container.DisposeAsync();
                    Console.WriteLine($"[{config.ContainerName}] Container disposed");
                }
                else
                {
                    Console.WriteLine(
                        $"[{config.ContainerName}] Keeping container running (TESTBED_KEEP_CONTAINERS=true)");
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
            new()
            {
                ContainerName = "TiDB",
                DatabaseProvider = "TiDB",
                Container = new TiDBTestContainer(),
                TestProviderFactory = (db, sp) => new TiDBTestProvider(db, sp)
            }
            // Add Sybase as needed
        };

        // Snowflake — requires external credentials; opt-in via INCLUDE_SNOWFLAKE=true
        if (_includeSnowflake)
        {
            configurations.Add(new TestConfiguration
            {
                ContainerName = "Snowflake",
                DatabaseProvider = "Snowflake",
                Container = new SnowflakeTestContainer(),
                TestProviderFactory = (db, sp) => new SnowflakeTestProvider(db, sp)
            });
        }

        if (_includeYugabyte)
        {
            configurations.Add(new TestConfiguration
            {
                ContainerName = "YugabyteDB",
                DatabaseProvider = "YugabyteDB",
                Container = new YugabyteTestContainer(),
                TestProviderFactory = (db, sp) => new YugabyteTestProvider(db, sp)
            });
        }

        // Oracle - check if external Oracle is available
        if (_includeOracle)
        {
            configurations.Add(new TestConfiguration
            {
                ContainerName = "Oracle",
                DatabaseProvider = "Oracle",
                Container = new OracleTestContainer(), // Use managed Testcontainer
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

        // Results table — Startup = container ready time, Exec = RunTest() only
        Console.WriteLine(
            $"{"Container",-15} {"Provider",-12} {"Status",-8} {"Startup",-10} {"Exec",-10} {"Error",-30}");
        Console.WriteLine(new string('-', 80));

        foreach (var result in results)
        {
            var status = result.Success ? "✅ PASS" : "❌ FAIL";
            var startupTime = result.ContainerStartTime.TotalSeconds.ToString("F2") + "s";
            var execTime = result.TestTime.HasValue ? result.TestTime.Value.TotalSeconds.ToString("F2") + "s" : "N/A";
            var error = result.Error?.Substring(0, Math.Min(result.Error.Length, 30)) ?? "";

            Console.WriteLine(
                $"{result.ContainerName,-15} {result.DatabaseProvider,-12} {status,-8} {startupTime,-10} {execTime,-10} {error,-30}");
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

        var execTimes = results.Where(r => r.TestTime.HasValue).Select(r => r.TestTime!.Value.TotalSeconds).ToArray();
        if (execTimes.Length > 0)
        {
            Console.WriteLine($"Test execution — max: {execTimes.Max():F2}s  avg: {execTimes.Average():F2}s  total: {execTimes.Sum():F2}s");
        }
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
