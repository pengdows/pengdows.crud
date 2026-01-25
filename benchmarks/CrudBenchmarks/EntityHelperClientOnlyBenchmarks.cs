#nullable enable
using System.Data;
using BenchmarkDotNet.Attributes;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;

namespace CrudBenchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class EntityHelperClientOnlyBenchmarks
{
    private EntityHelper<Film, int> _helper = null!;
    private DatabaseContext _context = null!;
    private TypeMapRegistry _typeMap = null!;
    private IReadOnlyCollection<int> _ids = null!;

    [GlobalSetup]
    public void Setup()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<Film>();
        _context = new DatabaseContext("Data Source=:memory:", factory, _typeMap);
        _helper = new EntityHelper<Film, int>(_context);
        _ids = new[] { 1 };
    }

    [Benchmark]
    public string BuildRetrieve_Single()
    {
        using var sc = _helper.BuildRetrieve(_ids, _context);
        return sc.Query.ToString();
    }

    [Benchmark]
    public Film MapReader_Single()
    {
        using var reader = new TestTrackedReader(new Dictionary<string, object>
        {
            ["film_id"] = 1,
            ["title"] = "Film 1",
            ["length"] = 123
        });

        reader.Read();
        return _helper.MapReaderToObject(reader);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _context.Dispose();
    }

    [Table("film", "public")]
    public sealed class Film
    {
        [Id(false)]
        [Column("film_id", DbType.Int32)]
        public int Id { get; set; }

        [Column("title", DbType.String)] public string Title { get; set; } = string.Empty;

        [Column("length", DbType.Int32)] public int Length { get; set; }
    }

    private sealed class TestTrackedReader : fakeDbDataReader, ITrackedReader
    {
        public TestTrackedReader(Dictionary<string, object> row)
            : base(new[] { row })
        {
        }

        public new Task<bool> ReadAsync()
        {
            return base.ReadAsync(CancellationToken.None);
        }

        public new Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            return base.ReadAsync(cancellationToken);
        }
    }
}