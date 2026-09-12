using System.Collections.Concurrent;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using testbed.Cockroach;
using testbed.Db2;
using testbed.DuckDb;
using testbed.FlatFile;
using testbed.Firebird;
using testbed.Informix;
using testbed.mariaDb;
using testbed.MySQL;
using testbed.Oracle;
using testbed.PostgreSQL;
using testbed.SqlServer;
using testbed.SapHana;
using testbed.InterBase;
using testbed.Sybase;
using testbed.TiDB;
using testbed.Snowflake;
using testbed.Spanner;
using testbed.Yugabyte;

namespace testbed;

public class ParallelTestOrchestrator
{
    private readonly IServiceProvider _services;
    private readonly ConcurrentBag<TestResult> _results = new();
    private readonly bool _includeSnowflake;
    private readonly bool _includeSapHana;
    private readonly bool _includeInterBase;

    public ParallelTestOrchestrator(IServiceProvider services, bool includeSnowflake = false, bool includeSapHana = false, bool includeInterBase = false)
    {
        _services = services;
        _includeSnowflake = includeSnowflake;
        _includeSapHana = includeSapHana;
        _includeInterBase = includeInterBase;
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
            SupportedDatabase.FlatFile => new FlatFileTestContainer(),
            SupportedDatabase.PostgreSql => new PostgreSqlTestContainer(),
            SupportedDatabase.Spanner => new SpannerOmniTestContainer(),
            SupportedDatabase.SqlServer => new SqlServerTestContainer(),
            SupportedDatabase.MySql => new MySqlTestContainer(),
            SupportedDatabase.MariaDb => new MariaDbContainer(),
            SupportedDatabase.Oracle => new OracleTestContainer(),
            SupportedDatabase.Firebird => new FirebirdSqlTestContainer(),
            SupportedDatabase.CockroachDb => new CockroachDbTestContainer(),
            SupportedDatabase.DuckDB => new DuckDbTestContainer(),
            SupportedDatabase.YugabyteDb => new YugabyteTestContainer(),
            SupportedDatabase.TiDb => new TiDBTestContainer(),
            SupportedDatabase.Db2 => new Db2TestContainer(),
            SupportedDatabase.SybaseASE => new SybaseTestContainer(),
            SupportedDatabase.Snowflake when _includeSnowflake => new SnowflakeTestContainer(),
            SupportedDatabase.Informix => new InformixTestContainer(),
            SupportedDatabase.SapHana when _includeSapHana => new HanaTestContainer(),
            SupportedDatabase.InterBase when _includeInterBase => new InterBaseTestContainer(),
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
        ISet<string>? exclude = null,
        ISet<string>? versions = null)
    {
        var testConfigurations = GetTestConfigurations(only, exclude, versions);

        // Dispatch order matters even though only 2 run at a time: this is a FIFO queue drained by
        // a 2-slot semaphore, so whichever slot frees first immediately grabs the NEXT item in
        // list order. Sorting the slowest-starting containers (e.g. Db2, Oracle) to the front means
        // both slots start on heavy work at t=0 instead of Db2 sitting queued behind 9 other
        // configurations and not even beginning its own slow image pull until they've all cycled
        // through — which previously made a single Db2 run dominate total wall-clock time.
        testConfigurations = OrderByStartupWeightDescending(testConfigurations);

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
            StartTime = startTime,
            ConfiguredStartupWeightSeconds = config.StartupWeightSeconds
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
            result.ChecksPassed = testProvider.ChecksPassed;
            result.ChecksSkipped = testProvider.ChecksSkipped;
            result.Checks = testProvider.Checks;

            Console.WriteLine(
                $"[{config.ContainerName}] ✅ Tests completed in {result.TestTime.Value.TotalSeconds:F2}s");
        }
        catch (TimeoutException tex)
        {
            result.Success = false;
            result.ContainerStartTimeout = true;
            result.Error = tex.Message;
            result.TotalTime = DateTime.UtcNow - startTime;

            Console.WriteLine($"[{config.ContainerName}] ⚠️ Unavailable: {tex.Message}");
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

    // POLICY: Every new SupportedDatabase value requires an entry in this list.
    /// <summary>
    /// Sorts by descending <see cref="TestConfiguration.StartupWeightSeconds"/>, stable for ties.
    /// Pure and side-effect-free — used both by <see cref="RunAllTestsAsync"/> and directly by
    /// tests, since it never touches a container or Docker.
    /// </summary>
    public static List<TestConfiguration> OrderByStartupWeightDescending(IEnumerable<TestConfiguration> configs)
    {
        return configs.OrderByDescending(c => c.StartupWeightSeconds).ToList();
    }

    /// <summary>
    /// Shapes a completed run's <see cref="TestResult"/>s into spinup/exercise/setup-overhead/total
    /// rows, sorted by measured spinup descending — the same ordering signal
    /// <see cref="TestConfiguration.StartupWeightSeconds"/> currently has to guess at by hand, so a
    /// real run's numbers can be read off directly instead of re-derived from scrollback. Pure and
    /// static specifically so it's unit-testable without Docker (see
    /// <c>ParallelTestOrchestratorTimingBreakdownTests</c>).
    /// </summary>
    public static IReadOnlyList<TimingBreakdownRow> BuildTimingBreakdown(IReadOnlyList<TestResult> results)
    {
        return results
            .Select(r =>
            {
                var spinup = r.ContainerStartTime.TotalSeconds;
                var exercise = r.TestTime?.TotalSeconds;
                var total = r.TotalTime.TotalSeconds;
                // Floored at 0 rather than reporting total-minus-spinup when there's no exercise
                // time at all (e.g. a container-start timeout) — that value would just be "time
                // spent failing to connect", not setup overhead.
                var setupOverhead = exercise.HasValue ? Math.Max(0, total - spinup - exercise.Value) : 0;

                return new TimingBreakdownRow(
                    r.ContainerName,
                    r.DatabaseProvider,
                    r.ConfiguredStartupWeightSeconds,
                    spinup,
                    exercise,
                    setupOverhead,
                    total,
                    r.Success);
            })
            .OrderByDescending(row => row.SpinupSeconds)
            .ToList();
    }

    // Snowflake is opt-in for credentials (cloud-only, no Docker image). SAP HANA is opt-in for a
    // different reason: saplabs/hanaexpress is a real Docker image, but a working container needs
    // 16-32GB RAM per SAP's own guidance — far beyond a standard CI runner and every other
    // database here. All other databases must appear unconditionally. See CLAUDE.md
    // "Adding a New Database".
    public List<TestConfiguration> GetTestConfigurations(
        ISet<string>? only = null,
        ISet<string>? exclude = null,
        ISet<string>? versions = null)
    {
        var configurations = new List<TestConfiguration>();

        void AddLocal(string provider, ITestContainer container, Func<IDatabaseContext, IServiceProvider, TestProvider> factory, int weight)
        {
            configurations.Add(new TestConfiguration
            {
                ContainerName = provider, DatabaseProvider = provider, DatabaseVersion = "local", Image = null,
                Container = container, TestProviderFactory = factory, StartupWeightSeconds = weight
            });
        }

        void AddDocker(string provider, int weight, Func<string, ITestContainer> containerFactory, Func<IDatabaseContext, IServiceProvider, TestProvider> factory)
        {
            foreach (var version in TestbedImageMatrix.Get(provider))
            {
                if (versions is { Count: > 0 } && !versions.Contains(version.Label) && !versions.Contains(version.Image))
                    continue;

                configurations.Add(new TestConfiguration
                {
                    ContainerName = $"{provider} [{version.Label}]", DatabaseProvider = provider,
                    DatabaseVersion = version.Label, Image = version.Image,
                    Container = containerFactory(version.Image), TestProviderFactory = factory,
                    StartupWeightSeconds = weight
                });
            }
        }

        AddLocal("SQLite", new SqliteTestContainer(), (db, sp) => new SqliteTestProvider(db, sp), 1);
        AddLocal("DuckDB", new DuckDbTestContainer(), (db, sp) => new DuckDbTestProvider(db, sp), 1);
        AddLocal("FlatFile", new FlatFileTestContainer(), (db, sp) => new FlatFileTestProvider(db, sp), 1);
        AddDocker("PostgreSQL", 5, image => image.Contains("fujitsu", StringComparison.OrdinalIgnoreCase)
                ? new PostgreSqlTestContainer(image, port: 27500, useAdminPasswordEnvVar: true)
                : new PostgreSqlTestContainer(image),
            (db, sp) => new PostgreSQLTestProvider(db, sp));
        AddLocal("Spanner", new SpannerOmniTestContainer(), (db, sp) => new SpannerTestProvider(db, sp), 30);
        AddDocker("MySQL", 8, image => new MySqlTestContainer(image), (db, sp) => new TestProvider(db, sp));
        AddDocker("MariaDB", 8, image => new MariaDbContainer(image), (db, sp) => new MariaDbTestProvider(db, sp));
        AddDocker("SQL Server", 25, image => new SqlServerTestContainer(image), (db, sp) => new SqlServerTestProvider(db, sp));
        AddDocker("CockroachDB", 12, image => new CockroachDbTestContainer(image), (db, sp) => new CockroachDbTestProvider(db, sp));
        AddDocker("Firebird", 8, image => new FirebirdSqlTestContainer(image), (db, sp) => new FirebirdTestProvider(db, sp));
        AddDocker("TiDB", 20, image => new TiDBTestContainer(image), (db, sp) => new TiDBTestProvider(db, sp));
        AddDocker("YugabyteDB", 20, image => new YugabyteTestContainer(image), (db, sp) => new YugabyteTestProvider(db, sp));
        AddDocker("Oracle", 45, image => new OracleTestContainer(image), (db, sp) => new OracleTestProvider(db, sp));
        AddDocker("Db2", 60, image => new Db2TestContainer(image), (db, sp) => new Db2TestProvider(db, sp));
        // SybaseTestContainer has no per-image constructor (unlike AddDocker's providers) — it
        // pins one verified image internally (nguoianphu/docker-sybase) and handles ASE 16's
        // SIGSEGV-on-boot workaround (SAP KBA 3018138: trace flag -T11889 + restart) as part of
        // its own startup sequence, so it goes through AddLocal like SQLite/DuckDB/Snowflake.
        AddLocal("Sybase ASE", new SybaseTestContainer(), (db, sp) => new SybaseTestProvider(db, sp), 45);

        // Confirmed live end-to-end (icr.io/informix/informix-developer-database pulls
        // anonymously, no registry credentials required — verified with a fresh
        // `docker rmi`+`docker pull` with no ~/.docker/config.json present): full CRUD,
        // transactions, concurrency, error mapping, and capability probes all pass — see
        // InformixDialect.cs's SupportsMerge/SupportsOffsetFetch comments for the two
        // confirmed-unsupported capabilities and TestProvider.cs's Informix-specific skips for
        // the rest. Promoted from opt-in to unconditional, matching Sybase's precedent.
        AddLocal("Informix", new InformixTestContainer(), (db, sp) => new InformixTestProvider(db, sp), 20);

        if (_includeSnowflake)
            AddLocal("Snowflake", new SnowflakeTestContainer(), (db, sp) => new SnowflakeTestProvider(db, sp), 5);

        // Weight 300: CONFIRMED live to take several minutes even with the image already pulled
        // locally — by far the slowest of any database here (compare Db2's 60s weight, itself the
        // previous high-water mark). Single pinned image internally, like Sybase/Informix.
        if (_includeSapHana)
            AddLocal("SAP HANA", new HanaTestContainer(), (db, sp) => new HanaTestProvider(db, sp), 300);

        // Weight 5: connecting to an already-running, externally-managed container (see
        // InterBaseTestContainer.cs's class remarks) — no image pull/first-boot cost like every
        // other database here, just a connection retry loop.
        if (_includeInterBase)
            AddLocal("InterBase", new InterBaseTestContainer(), (db, sp) => new InterBaseTestProvider(db, sp), 5);

        if (only is { Count: > 0 })
            configurations = configurations.Where(c => only.Contains(c.ContainerName, StringComparer.OrdinalIgnoreCase) || only.Contains(c.DatabaseProvider, StringComparer.OrdinalIgnoreCase)).ToList();
        if (exclude is { Count: > 0 })
            configurations = configurations.Where(c => !exclude.Contains(c.ContainerName, StringComparer.OrdinalIgnoreCase) && !exclude.Contains(c.DatabaseProvider, StringComparer.OrdinalIgnoreCase)).ToList();

        return configurations;
    }

    private void DisplayResults()
    {
        const int W = 84;
        Console.WriteLine("\n" + new string('=', W));
        Console.WriteLine("TEST RESULTS SUMMARY");
        Console.WriteLine(new string('=', W));

        var results = _results.OrderBy(r => r.ContainerName).ToArray();
        var totalPassed = results.Count(r => r.Success);
        var totalUnavailable = results.Count(r => r.ContainerStartTimeout);
        var totalFailed = results.Length - totalPassed - totalUnavailable;
        var totalChecks = results.Sum(r => r.ChecksPassed);
        var totalSkipped = results.Sum(r => r.ChecksSkipped);

        // Header
        Console.WriteLine($"{"Database",-18} {"Pass",5} {"Fail",5} {"Skip",5}  {"Time",8}  Status");
        Console.WriteLine(new string('-', W));

        foreach (var r in results)
        {
            var passCol = r.Success ? r.ChecksPassed.ToString() : r.ChecksPassed.ToString();
            var failCol = r.Success ? "-" : (r.ContainerStartTimeout ? "⚠️" : "❌");
            var skipCol = r.ChecksSkipped > 0 ? r.ChecksSkipped.ToString() : "-";
            var timeCol = r.TestTime.HasValue ? r.TestTime.Value.TotalSeconds.ToString("F2") + "s" : "-";
            var status = r.Success
                ? "✅"
                : r.ContainerStartTimeout
                    ? $"⚠️  Unavailable ({r.Error?[..Math.Min(r.Error.Length, 30)] ?? ""})"
                    : $"❌  {r.Error?[..Math.Min(r.Error.Length, 38)] ?? ""}";

            Console.WriteLine($"{r.ContainerName,-18} {passCol,5} {failCol,5} {skipCol,5}  {timeCol,8}  {status}");
        }

        // Totals row
        Console.WriteLine(new string('-', W));
        var execTimes = results.Where(r => r.TestTime.HasValue).Select(r => r.TestTime!.Value.TotalSeconds).ToArray();
        var totalTime = execTimes.Length > 0 ? execTimes.Sum().ToString("F2") + "s" : "-";
        Console.WriteLine($"{"TOTAL",-18} {totalChecks,5} {totalFailed,5} {totalSkipped,5}  {totalTime,8}  {totalPassed}/{results.Length} databases");
        Console.WriteLine(new string('=', W));

        if (totalFailed > 0)
        {
            Console.WriteLine("\nFAILURES:");
            foreach (var f in results.Where(r => !r.Success && !r.ContainerStartTimeout))
                Console.WriteLine($"  {f.ContainerName}: {f.Error}");
            Console.WriteLine();
        }

        if (totalUnavailable > 0)
        {
            Console.WriteLine("\nUNAVAILABLE (container startup timed out — not a test failure):");
            foreach (var f in results.Where(r => r.ContainerStartTimeout))
                Console.WriteLine($"  {f.ContainerName}: {f.Error}");
            Console.WriteLine();
        }

        DisplayTimingBreakdown(results);
        EmitJsonResults(results);
    }

    /// <summary>
    /// Prints spinup/exercise/setup-overhead/total per database, sorted by measured spinup
    /// descending, next to the configured <see cref="TestConfiguration.StartupWeightSeconds"/>
    /// dispatch-order guess — so a live run's real numbers can directly correct that guess instead
    /// of being re-derived from scrollback. See <see cref="BuildTimingBreakdown"/>.
    /// </summary>
    private static void DisplayTimingBreakdown(IReadOnlyList<TestResult> results)
    {
        const int w = 92;
        Console.WriteLine("\n" + new string('=', w));
        Console.WriteLine("TIMING BREAKDOWN (sorted by measured spinup, slowest first)");
        Console.WriteLine(new string('=', w));
        Console.WriteLine(
            $"{"Database",-18} {"Spinup",8} {"Weight*",8} {"Exercise",8} {"Setup",8} {"Total",8}");
        Console.WriteLine(new string('-', w));

        foreach (var row in BuildTimingBreakdown(results))
        {
            var exerciseCol = row.ExerciseSeconds.HasValue ? row.ExerciseSeconds.Value.ToString("F2") + "s" : "-";
            Console.WriteLine(
                $"{row.ContainerName,-18} {row.SpinupSeconds,7:F2}s {row.ConfiguredWeightSeconds,7:F0}s {exerciseCol,8} {row.SetupOverheadSeconds,7:F2}s {row.TotalSeconds,7:F2}s");
        }

        Console.WriteLine(new string('-', w));
        Console.WriteLine("* Weight = configured StartupWeightSeconds this run's dispatch order was actually");
        Console.WriteLine("  sorted by. A measured Spinup well above Weight means the dispatch-order guess for");
        Console.WriteLine("  that database should be raised so it claims a slot earlier next run.");
        Console.WriteLine(new string('=', w));
    }

    private static void EmitJsonResults(IReadOnlyList<TestResult> results)
    {
        try
        {
            var output = results.Select(r => new
            {
                engine = r.DatabaseProvider,
                container = r.ContainerName,
                success = r.Success,
                outcome = r.Success ? "Passed" : (r.ContainerStartTimeout ? "Unavailable" : "Failed"),
                checksAttempted = r.ChecksPassed + (r.Success ? 0 : 1),
                checksPassed = r.ChecksPassed,
                checksFailed = r.Success ? 0 : 1,
                checksSkipped = r.ChecksSkipped,
                totalChecks = r.ChecksPassed + (r.Success ? 0 : 1) + r.ChecksSkipped,
                containerStartTimeSeconds = r.ContainerStartTime.TotalSeconds,
                configuredStartupWeightSeconds = r.ConfiguredStartupWeightSeconds,
                testTimeSeconds = r.TestTime?.TotalSeconds,
                totalTimeSeconds = r.TotalTime.TotalSeconds,
                error = r.Error,
                checks = r.Checks.Select(c => new
                {
                    name = c.Name,
                    outcome = c.Outcome,
                    reason = c.Reason
                }).ToArray()
            }).ToList();

            var json = System.Text.Json.JsonSerializer.Serialize(output, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText("testbed-results.json", json);
            if (Directory.Exists("TestResults"))
            {
                File.WriteAllText(Path.Combine("TestResults", "testbed-results.json"), json);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Warning] Failed to write testbed-results.json: {ex.Message}");
        }
    }
}

public class TestConfiguration
{
    public required string ContainerName { get; set; }
    public required string DatabaseProvider { get; set; }
    public required ITestContainer Container { get; set; }
    public required Func<IDatabaseContext, IServiceProvider, TestProvider> TestProviderFactory { get; set; }

    /// <summary>
    /// Best-effort relative ranking of how long this database's container takes to become ready
    /// (image pull + startup + first-connection wait) — used only to pick <see cref="ParallelTestOrchestrator"/>'s
    /// dispatch order, not a measured guarantee. Higher runs first. Revisit using real
    /// <see cref="TestResult.ContainerStartTime"/> data from live runs as it accumulates.
    /// </summary>
    public int StartupWeightSeconds { get; set; }
    public string DatabaseVersion { get; set; } = "unknown";
    public string? Image { get; set; }
}

public sealed record ImageVersion(string Label, string Image);

public static class TestbedImageMatrix
{
    private static readonly IReadOnlyDictionary<string, ImageVersion[]> Defaults = new Dictionary<string, ImageVersion[]>(StringComparer.OrdinalIgnoreCase)
    {
        // Citus, TimescaleDB, and Fujitsu Enterprise Postgres are PostgreSQL forks/extensions,
        // not distinct SupportedDatabase values - DatabaseDetectionService already resolves all
        // three to SupportedDatabase.PostgreSql (their DataSourceProductName/version string still
        // says "PostgreSQL"), so they run through the exact same PostgreSQLTestProvider suite as
        // vanilla PostgreSQL. Verified live before adding here - see PostgreSqlTestContainer's
        // fujitsuPort/useAdminPasswordEnvVar overrides for the two real deployment quirks Fujitsu
        // Enterprise Postgres needed (nonstandard port, PG_USER collides with initdb's own
        // "postgres" role).
        ["PostgreSQL"] = [
            new("16.4", "postgres:16.4"), new("15.0", "postgres:15.0"), new("9.5", "postgres:9.5-alpine"),
            new("Citus 12.1", "citusdata/citus:12.1"),
            new("TimescaleDB pg16", "timescale/timescaledb:latest-pg16"),
            new("Fujitsu Enterprise Postgres 17", "quay.io/fujitsu/fujitsu-enterprise-postgres-17-server:latest")
        ],
        // Percona Server for MySQL is a drop-in MySQL-compatible fork, not a distinct
        // SupportedDatabase value - it reports itself as "MySQL" (no "percona" token anywhere in
        // DataSourceProductName/version string), so DatabaseDetectionService already resolves it
        // to SupportedDatabase.MySql and it runs through the same suite as vanilla MySQL. Verified
        // live before adding here.
        ["MySQL"] = [new("8.4.11", "mysql:8.4.11"), new("8.0.36", "mysql:8.0.36"), new("5.7", "mysql:5.7"), new("Percona 8.0", "percona:8.0")],
        ["MariaDB"] = [new("11.4.12", "mariadb:11.4.12"), new("10.11.11", "mariadb:10.11.11"), new("10.4", "mariadb:10.4"), new("10.2", "mariadb:10.2")],
        ["SQL Server"] = [new("2022-CU25", "mcr.microsoft.com/mssql/server:2022-CU25-GDR2-ubuntu-22.04"), new("2019", "mcr.microsoft.com/mssql/server:2019-latest"), new("2017", "mcr.microsoft.com/mssql/server:2017-latest")],
        ["CockroachDB"] = [new("v25.1.0", "cockroachdb/cockroach:v25.1.0"), new("v24.3.0", "cockroachdb/cockroach:v24.3.0"), new("v23.2.14", "cockroachdb/cockroach:v23.2.14")],
        ["Firebird"] = [new("5.0.2", "firebirdsql/firebird:5.0.2"), new("4.0.5", "firebirdsql/firebird:4.0.5"), new("3.0.9", "firebirdsql/firebird:3.0.9")],
        ["TiDB"] = [new("v8.5.7", "pingcap/tidb:v8.5.7"), new("v7.5.7", "pingcap/tidb:v7.5.7")],
        ["YugabyteDB"] = [new("2025.2.5.2-b5", "yugabytedb/yugabyte:2025.2.5.2-b5"), new("2.25.2.0-b359", "yugabytedb/yugabyte:2.25.2.0-b359")],
        ["Oracle"] = [new("23.26.2", "gvenzl/oracle-free:23.26.2-slim-faststart"), new("21c", "gvenzl/oracle-xe:21-slim-faststart"), new("18c", "gvenzl/oracle-xe:18.4.0-slim-faststart")],
        ["Db2"] = [new("11.5.8.0", "ibmcom/db2:11.5.8.0"), new("11.5.0.0a", "ibmcom/db2:11.5.0.0a")]
    };

    public static IReadOnlyList<ImageVersion> Get(string provider)
    {
        var key = provider.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var overrideValue = Environment.GetEnvironmentVariable($"TESTBED_{key}_IMAGES");
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            var images = overrideValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return images.Select((image, index) => new ImageVersion($"custom-{index + 1}", image)).ToArray();
        }

        return Defaults.TryGetValue(provider, out var versions) ? versions : Array.Empty<ImageVersion>();
    }
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
    public bool ContainerStartTimeout { get; set; }
    public string? Error { get; set; }
    public int ChecksPassed { get; set; }
    public int ChecksSkipped { get; set; }
    public IReadOnlyList<CheckResult> Checks { get; set; } = Array.Empty<CheckResult>();

    /// <summary>
    /// The hand-set <see cref="TestConfiguration.StartupWeightSeconds"/> this run's dispatch order
    /// was actually sorted by — carried onto the result purely so a live run's measured
    /// <see cref="ContainerStartTime"/> can be compared against the guess that decided when this
    /// container got a dispatch slot (see <see cref="ParallelTestOrchestrator.BuildTimingBreakdown"/>).
    /// </summary>
    public int ConfiguredStartupWeightSeconds { get; set; }
}

/// <summary>
/// One row of <see cref="ParallelTestOrchestrator.BuildTimingBreakdown"/>'s output: a database's
/// measured spinup (container startup), exercise (the test provider's own run), setup overhead
/// (whatever's left — <see cref="IDatabaseContext"/> creation, test-provider construction — not
/// captured by either stopwatch), and total, alongside the configured dispatch-order weight it was
/// actually sorted by.
/// </summary>
public sealed record TimingBreakdownRow(
    string ContainerName,
    string DatabaseProvider,
    double ConfiguredWeightSeconds,
    double SpinupSeconds,
    double? ExerciseSeconds,
    double SetupOverheadSeconds,
    double TotalSeconds,
    bool Success);
