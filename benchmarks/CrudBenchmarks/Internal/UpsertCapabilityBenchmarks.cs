using System.Data;
using BenchmarkDotNet.Attributes;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

namespace CrudBenchmarks.Internal;

/// <summary>
/// Isolates the cost of BuildUpsert's dialect-capability branch checks — this session's refactor
/// replaced hardcoded <c>dialect.DatabaseType == SupportedDatabase.X</c> enum comparisons with
/// <see cref="pengdows.crud.dialects.ISqlDialect.EmitsAnsiMergeSyntax"/> /
/// <see cref="pengdows.crud.dialects.ISqlDialect.SupportsPureKeyUpsert"/> property reads inside
/// <c>BuildUpsertMerge</c>. SQL Server takes the "standard ANSI MERGE" branch
/// (<c>EmitsAnsiMergeSyntax == true</c>); Firebird takes the "MATCHING" branch
/// (<c>EmitsAnsiMergeSyntax == false</c>) — together they exercise both sides of the changed
/// conditional on every call, with no real database involved (fakeDb only).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 20, invocationCount: 8192)]
public class UpsertCapabilityBenchmarks
{
    private TableGateway<UpsertEntity, int> _sqlServerGateway = null!;
    private TableGateway<UpsertEntity, int> _firebirdGateway = null!;
    private int _counter;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _sqlServerGateway = new TableGateway<UpsertEntity, int>(MakeContext(SupportedDatabase.SqlServer));
        _firebirdGateway = new TableGateway<UpsertEntity, int>(MakeContext(SupportedDatabase.Firebird));
    }

    private static DatabaseContext MakeContext(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "fake",
            ProviderName = db.ToString(),
            DbMode = DbMode.Standard
        };
        return new DatabaseContext(config, factory);
    }

    [Benchmark(Baseline = true)]
    public ISqlContainer BuildUpsert_SqlServer_AnsiMerge()
    {
        return _sqlServerGateway.BuildUpsert(new UpsertEntity { Id = _counter++, Name = "row" });
    }

    [Benchmark]
    public ISqlContainer BuildUpsert_Firebird_MatchingMerge()
    {
        return _firebirdGateway.BuildUpsert(new UpsertEntity { Id = _counter++, Name = "row" });
    }

    [Table("upsert_entity")]
    public sealed class UpsertEntity
    {
        [PrimaryKey(1)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }
}
