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
public class DirectViewBenchmarks
{
    private static readonly IndexedViewEnvironment Environment = new();
    private const string BenchmarkAppName = "Benchmark_DirectView";

    private readonly string[] _sessionStatements = new[]
    {
        "SET ARITHABORT ON",
        "SET ANSI_WARNINGS ON",
        "SET ANSI_NULLS ON",
        "SET QUOTED_IDENTIFIER ON",
        "SET CONCAT_NULL_YIELDS_NULL ON",
        "SET NUMERIC_ROUNDABORT OFF"
    };

    private DatabaseContext? _pengdowsContext;
    private TableGateway<IndexedViewBenchmarks.CustomerOrderSummary, int>? _summaryGateway;
    private SqlServerValidationResult? _validation;

    private SqlConnection? _hotConnection;

    private static string ViewSql => Environment.BuildViewSql(param => $"@{param}");
    private static string ViewLiteralSql => Environment.BuildViewSql(_ => Environment.SampleCustomerId.ToString());

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        await Environment.InitializeAsync();

        var map = new TypeMapRegistry();
        map.Register<IndexedViewBenchmarks.CustomerOrderSummary>();

        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = Environment.GetConnectionStringWithApplicationName(BenchmarkAppName),
            ReadWriteMode = ReadWriteMode.ReadWrite,
            DbMode = DbMode.Standard
        };

        _pengdowsContext = new DatabaseContext(cfg, SqlClientFactory.Instance, null, map);
        _summaryGateway = new TableGateway<IndexedViewBenchmarks.CustomerOrderSummary, int>(_pengdowsContext);

        _validation = await SqlServerBenchmarkValidation.ValidateAsync(new SqlServerValidationConfig
        {
            BenchmarkFamily = "DirectView",
            Variant = "ViewQuery",
            ConnectionString = Environment.ConnectionString,
            Sql = ViewLiteralSql,
            ViewSchema = "dbo",
            ViewName = "vw_CustomerOrderSummary",
            SessionSetup = connection => SqlServerSessionSettings.ApplyAsync(connection),
            RequiredSessionOptions = SqlServerSessionSettings.RequiredOptions,
            ExpectViewReference = true,
            ExpectedViewIndexName = Environment.ViewClusteredIndexName
        });
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_pengdowsContext is IAsyncDisposable candidate)
        {
            await candidate.DisposeAsync();
        }

        if (_hotConnection != null)
        {
            await _hotConnection.DisposeAsync();
            _hotConnection = null;
        }
    }

    [Benchmark]
    public async Task<IndexedViewBenchmarks.CustomerOrderSummary?> DirectView_Cold()
    {
        await using var connection = new SqlConnection(Environment.GetConnectionStringWithApplicationName("Benchmark_DirectView_Cold"));
        await connection.OpenAsync();
        await SqlServerSessionSettings.ApplyAsync(connection);

        return await connection.QuerySingleOrDefaultAsync<IndexedViewBenchmarks.CustomerOrderSummary>(
            ViewSql,
            new { customerId = Environment.SampleCustomerId });
    }

    [Benchmark]
    public async Task<IndexedViewBenchmarks.CustomerOrderSummary?> DirectView_Hot()
    {
        if (_hotConnection == null)
        {
            throw new InvalidOperationException("Hot connection was not initialized.");
        }

        return await _hotConnection.QuerySingleOrDefaultAsync<IndexedViewBenchmarks.CustomerOrderSummary>(
            ViewSql,
            new { customerId = Environment.SampleCustomerId });
    }

    [GlobalSetup(Target = nameof(DirectView_Hot))]
    public async Task SetupHotConnection()
    {
        _hotConnection = new SqlConnection(Environment.GetConnectionStringWithApplicationName("Benchmark_DirectView_Hot"));
        await _hotConnection.OpenAsync();
        await SqlServerSessionSettings.ApplyAsync(_hotConnection);
    }
}
