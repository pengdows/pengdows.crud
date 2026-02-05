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

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        await Environment.InitializeAsync();

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

        _manualValidation = await SqlServerBenchmarkValidation.ValidateAsync(new SqlServerValidationConfig
        {
            BenchmarkFamily = "AutoSubstitution",
            Variant = "ManualSetup",
            ConnectionString = Environment.ConnectionString,
            Sql = BaseSqlLiteral,
            ViewSchema = "dbo",
            ViewName = "vw_CustomerOrderSummary",
            SessionSetup = connection => SqlServerSessionSettings.ApplyAsync(connection),
            RequiredSessionOptions = SqlServerSessionSettings.RequiredOptions,
            ExpectViewReference = true,
            ExpectedViewIndexName = Environment.ViewClusteredIndexName
        });

        _autoValidation = await SqlServerBenchmarkValidation.ValidateAsync(new SqlServerValidationConfig
        {
            BenchmarkFamily = "AutoSubstitution",
            Variant = "PengdowsAuto",
            ConnectionString = Environment.ConnectionString,
            Sql = BaseSqlLiteral,
            ViewSchema = "dbo",
            ViewName = "vw_CustomerOrderSummary",
            RequiredSessionOptions = SqlServerSessionSettings.RequiredOptions,
            ExpectViewReference = true,
            ExpectedViewIndexName = Environment.ViewClusteredIndexName
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

    [GlobalSetup(Target = nameof(NoSetup_Hot))]
    public async Task SetupNoSetupHot()
    {
        _noSetupHotConnection = new SqlConnection(Environment.GetConnectionStringWithApplicationName(NoSetupHotName));
        await _noSetupHotConnection.OpenAsync();
    }

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

    [GlobalSetup(Target = nameof(ManualSetup_Hot))]
    public async Task SetupManualHot()
    {
        _manualHotConnection = new SqlConnection(Environment.GetConnectionStringWithApplicationName(ManualHotName));
        await _manualHotConnection.OpenAsync();
        await SqlServerSessionSettings.ApplyAsync(_manualHotConnection);
    }

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

    [GlobalSetup(Target = nameof(PengdowsAuto_Hot))]
    public void SetupPengdowsHot()
    {
        if (_pengdowsContext == null)
        {
            throw new InvalidOperationException("Pengdows context not ready for hot setup.");
        }

        _hotPengdowsContainer = _pengdowsContext.CreateSqlContainer();
    }
}
