using System;
using System.Data;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.SqlClient;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;

namespace CrudBenchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class AutoSubstitutionBenchmarks : IAsyncDisposable
{
    private static readonly IndexedViewEnvironment Environment = new();
    private const string NoSetupHotName = "Benchmark_Auto_NoSetup_Hot";
    private const string ManualHotName = "Benchmark_Auto_Manual_Hot";
    private const string PengdowsAppName = "Benchmark_Auto_Pengdows";

    private DatabaseContext? _pengdowsContext;
    private TableGateway<IndexedViewBenchmarks.CustomerOrderSummary, int>? _gateway;

    private SqlServerValidationResult? _noSetupValidation;
    private SqlServerValidationResult? _manualValidation;
    private SqlServerValidationResult? _autoValidation;

    private SqlConnection? _noSetupHotConnection;
    private SqlConnection? _manualHotConnection;
    private ISqlContainer? _hotPengdowsContainer;

    private static string BaseSqlParam => Environment.BuildBaseAggregateSql(param => $"@{param}");
    private static string BaseSqlLiteral => Environment.BuildBaseAggregateSql(_ => Environment.SampleCustomerId.ToString());

    // BenchmarkDotNet runs each benchmark method in its own process.  A
    // target-specific [GlobalSetup(Target = ...)] REPLACES the default
    // [GlobalSetup] for that target — it does not supplement it.  Every
    // GlobalSetup variant must therefore call this method to start the
    // container and wire up the pengdows context.  Do not remove these calls.
    private async Task EnsureInitializedAsync()
    {
        await Environment.InitializeAsync();

        if (_pengdowsContext != null) return;

        var map = new TypeMapRegistry();
        map.Register<IndexedViewBenchmarks.CustomerOrderSummary>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = Environment.GetConnectionStringWithApplicationName(PengdowsAppName),
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };

        _pengdowsContext = new DatabaseContext(cfg, SqlClientFactory.Instance, null, map);
        _gateway = new TableGateway<IndexedViewBenchmarks.CustomerOrderSummary, int>(_pengdowsContext);
    }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        await EnsureInitializedAsync();

        // NoSetup: raw Dapper on a fresh connection with zero session
        // configuration.  SQL Server requires ARITHABORT ON (among others) for
        // automatic indexed-view matching; without it the optimizer CANNOT use
        // the view.  ExpectNoViewReference + ProhibitedTableReferences enforce
        // that contract and will fail the benchmark if the plan ever touches the
        // view.  Do not remove or weaken these assertions — they are the proof
        // that unmanaged connections cannot reach the indexed view.
        _noSetupValidation = await SqlServerBenchmarkValidation.ValidateAsync(new SqlServerValidationConfig
        {
            BenchmarkFamily = "AutoSubstitution",
            Variant = "NoSetup",
            ConnectionString = Environment.ConnectionString,
            Sql = BaseSqlLiteral,
            ViewSchema = "dbo",
            ViewName = "vw_CustomerOrderSummary",
            ExpectNoViewReference = true,
            ProhibitedTableReferences = new[] { "vw_CustomerOrderSummary" }
        });

        // ManualSetup: Dapper with session settings applied by hand via SET
        // statements.  ARITHABORT is now ON so auto-matching is *eligible*, but
        // the optimizer is free to choose the base-table path when it calculates
        // lower cost (routine on small datasets with a covering index on Orders).
        // Do NOT add ExpectViewReference — it would be flaky.  What we validate
        // here is that every SET statement actually took effect
        // (RequiredSessionOptions).  This is the Dapper developer's manual
        // baseline; pengdows must match or beat it without any hand-written SETs.
        _manualValidation = await SqlServerBenchmarkValidation.ValidateAsync(new SqlServerValidationConfig
        {
            BenchmarkFamily = "AutoSubstitution",
            Variant = "ManualSetup",
            ConnectionString = Environment.ConnectionString,
            Sql = BaseSqlLiteral,
            ViewSchema = "dbo",
            ViewName = "vw_CustomerOrderSummary",
            SessionSetup = connection => SqlServerSessionSettings.ApplyAsync(connection),
            RequiredSessionOptions = SqlServerSessionSettings.RequiredOptions
        });

        // PengdowsAuto: pengdows.crud applies the same session settings
        // automatically via DatabaseContext — no manual SET statements required.
        // This is the correctness story: pengdows gets ARITHABORT ON (and the
        // rest) for free; Dapper and raw ADO do not.  Same optimizer caveat as
        // ManualSetup — do not add ExpectViewReference.  RequiredSessionOptions
        // confirms the automatic settings are byte-for-byte identical to what
        // ManualSetup had to produce by hand.
        _autoValidation = await SqlServerBenchmarkValidation.ValidateAsync(new SqlServerValidationConfig
        {
            BenchmarkFamily = "AutoSubstitution",
            Variant = "PengdowsAuto",
            ConnectionString = Environment.ConnectionString,
            Sql = BaseSqlLiteral,
            ViewSchema = "dbo",
            ViewName = "vw_CustomerOrderSummary",
            SessionSetup = connection => SqlServerSessionSettings.ApplyAsync(connection),
            RequiredSessionOptions = SqlServerSessionSettings.RequiredOptions
        });
    }

    [GlobalCleanup]
    public async ValueTask DisposeAsync()
    {
        if (_pengdowsContext is IAsyncDisposable candidate)
        {
            await candidate.DisposeAsync();
        }

        if (_noSetupHotConnection != null)
        {
            await _noSetupHotConnection.DisposeAsync();
            _noSetupHotConnection = null;
        }

        if (_manualHotConnection != null)
        {
            await _manualHotConnection.DisposeAsync();
            _manualHotConnection = null;
        }

        if (_hotPengdowsContainer != null)
        {
            await _hotPengdowsContainer.DisposeAsync();
            _hotPengdowsContainer = null;
        }
    }

    // --- NoSetup: Dapper with no session settings (worst case) -----------
    // Proves that without ARITHABORT ON the indexed view is unreachable.

    [Benchmark]
    public async Task<IndexedViewBenchmarks.CustomerOrderSummary?> NoSetup_Cold()
    {
        await using var connection = new SqlConnection(Environment.GetConnectionStringWithApplicationName("Benchmark_Auto_NoSetup_Cold"));
        await connection.OpenAsync();

        return await connection.QuerySingleOrDefaultAsync<IndexedViewBenchmarks.CustomerOrderSummary>(
            BaseSqlParam,
            new { customerId = Environment.SampleCustomerId });
    }

    [Benchmark]
    public async Task<IndexedViewBenchmarks.CustomerOrderSummary?> NoSetup_Hot()
    {
        if (_noSetupHotConnection == null)
        {
            throw new InvalidOperationException("No-setup hot connection was not initialized.");
        }

        return await _noSetupHotConnection.QuerySingleOrDefaultAsync<IndexedViewBenchmarks.CustomerOrderSummary>(
            BaseSqlParam,
            new { customerId = Environment.SampleCustomerId });
    }

    // Target-specific GlobalSetup: replaces (does not supplement) the default.
    // Must call EnsureInitializedAsync — see comment above that method.
    [GlobalSetup(Target = nameof(NoSetup_Hot))]
    public async Task SetupNoSetupHot()
    {
        await EnsureInitializedAsync();
        _noSetupHotConnection = new SqlConnection(Environment.GetConnectionStringWithApplicationName(NoSetupHotName));
        await _noSetupHotConnection.OpenAsync();
    }

    // --- ManualSetup: Dapper with hand-written SET statements -------------
    // Cold path pays the SET overhead on every connection open — this is what
    // every Dapper/ADO developer must do manually to be eligible for auto-
    // matching.  pengdows does this automatically.

    [Benchmark]
    public async Task<IndexedViewBenchmarks.CustomerOrderSummary?> ManualSetup_Cold()
    {
        await using var connection = new SqlConnection(Environment.GetConnectionStringWithApplicationName("Benchmark_Auto_Manual_Cold"));
        await connection.OpenAsync();
        await SqlServerSessionSettings.ApplyAsync(connection);

        return await connection.QuerySingleOrDefaultAsync<IndexedViewBenchmarks.CustomerOrderSummary>(
            BaseSqlParam,
            new { customerId = Environment.SampleCustomerId });
    }

    [Benchmark]
    public async Task<IndexedViewBenchmarks.CustomerOrderSummary?> ManualSetup_Hot()
    {
        if (_manualHotConnection == null)
        {
            throw new InvalidOperationException("Manual hot connection was not initialized.");
        }

        return await _manualHotConnection.QuerySingleOrDefaultAsync<IndexedViewBenchmarks.CustomerOrderSummary>(
            BaseSqlParam,
            new { customerId = Environment.SampleCustomerId });
    }

    // Target-specific GlobalSetup: replaces (does not supplement) the default.
    // Must call EnsureInitializedAsync — see comment above that method.
    [GlobalSetup(Target = nameof(ManualSetup_Hot))]
    public async Task SetupManualHot()
    {
        await EnsureInitializedAsync();
        _manualHotConnection = new SqlConnection(Environment.GetConnectionStringWithApplicationName(ManualHotName));
        await _manualHotConnection.OpenAsync();
        await SqlServerSessionSettings.ApplyAsync(_manualHotConnection);
    }

    // --- PengdowsAuto: pengdows.crud with automatic session management ------
    // Session settings are applied by DatabaseContext, not by the benchmark.
    // The Cold path here should be comparable to ManualSetup_Hot (not Cold)
    // because the SET overhead is handled once by the context, not per-call.

    [Benchmark]
    public async Task<IndexedViewBenchmarks.CustomerOrderSummary?> PengdowsAuto_Cold()
    {
        if (_pengdowsContext == null || _gateway == null)
        {
            throw new InvalidOperationException("Pengdows context was not initialized.");
        }

        await using var container = _pengdowsContext.CreateSqlContainer();
        container.Query.Append(BaseSqlParam);
        container.AddParameterWithValue("customerId", DbType.Int32, Environment.SampleCustomerId);
        return await _gateway.LoadSingleAsync(container);
    }

    [Benchmark]
    public Task<IndexedViewBenchmarks.CustomerOrderSummary?> PengdowsAuto_Hot()
    {
        if (_hotPengdowsContainer == null || _gateway == null)
        {
            throw new InvalidOperationException("Hot pengdows container was not initialized.");
        }

        _hotPengdowsContainer.Clear();
        _hotPengdowsContainer.Query.Append(BaseSqlParam);
        _hotPengdowsContainer.AddParameterWithValue("customerId", DbType.Int32, Environment.SampleCustomerId);
        return _gateway.LoadSingleAsync(_hotPengdowsContainer);
    }

    // Target-specific GlobalSetup: replaces (does not supplement) the default.
    // Must call EnsureInitializedAsync — see comment above that method.
    [GlobalSetup(Target = nameof(PengdowsAuto_Hot))]
    public async Task SetupPengdowsHot()
    {
        await EnsureInitializedAsync();
        _hotPengdowsContainer = _pengdowsContext!.CreateSqlContainer();
    }
}
