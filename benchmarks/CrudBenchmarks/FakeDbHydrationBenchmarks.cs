using System.Data;
using System.Data.Common;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;

namespace CrudBenchmarks;

/// <summary>
/// Pure row-hydration CPU microscope for pengdows.crud's DataReaderMapper against Dapper.
///
/// There is deliberately no connection, command, SQL execution, provider, network, pool, transaction, or
/// SQL-generation work in any arm: each consumes a fresh fakeDb reader over the same prebuilt rows and declared
/// schema. fakeDb's getters are extremely cheap, so fixed per-call framework work looks large next to them; real
/// providers' decoding cost dilutes it. Read the numbers as a lower-bound overhead measurement, never as an
/// application-level comparison.
///
/// The arms answer three different questions, and only arms in the same category are comparable:
///   PublicApi  - the call a person makes. pengdows' LoadAsync against Dapper's parser lookup plus the same loop.
///                Both resolve their mapping on every call and neither pre-sizes its list (Dapper's own buffered
///                Query cannot know the row count either), so this includes plan/parser resolution and list growth.
///   WarmMapper - an already-resolved mapper, nothing else varying: one loop shape, a pre-sized list, and only the
///                mapper delegate differs. pengdows.crud has two mapper paths and each has its own arm:
///                Warm_Pengdows_ReaderMapperPlan (DataReaderMapper, arbitrary POCOs) and
///                Warm_Pengdows_CompiledMapper (CompiledMapperFactory, TableGateway entities; a
///                Func&lt;DbDataReader,T&gt;, the same type Dapper's parser has).
///                Warm_Pengdows_CompiledMapperSpecialized is a reference with a reader-type-specialized delegate and
///                is not a competitor to Dapper.
///   Resolution - the cost of finding the mapping for a reader whose schema is already warm: pengdows' shape and
///                plan lookup (empty reader through LoadAsync) against Dapper's GetRowParser.
/// The timed arms return a row count; results are verified in GlobalSetup and in the shape tests, not in the
/// timed path.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class FakeDbHydrationBenchmarks
{
    private static readonly IReadOnlyList<fakeDbColumn> Columns =
    [
        new("Id", typeof(int), "INTEGER"),
        new("LongId", typeof(long), "BIGINT"),
        new("Name", typeof(string), "TEXT"),
        new("Email", typeof(string), "TEXT"),
        new("Age", typeof(int), "INTEGER"),
        new("Salary", typeof(decimal), "DECIMAL"),
        new("IsActive", typeof(bool), "BOOLEAN"),
        new("CreatedOn", typeof(DateTime), "TIMESTAMP"),
        new("CreatedOffset", typeof(DateTimeOffset), "TIMESTAMP_TZ"),
        new("Score", typeof(double), "DOUBLE"),
        new("Ratio", typeof(float), "REAL"),
        new("CorrelationId", typeof(Guid), "UUID"),
        new("Payload", typeof(byte[]), "BLOB")
    ];

    private List<Dictionary<string, object>> _rows = null!;
    private Func<DbDataReader, HydrationEntity> _dapperParser = null!;
    private Func<DbDataReader, PlainHydrationPoco> _dapperPlainParser = null!;
    private Func<DbDataReader, HydrationEntity> _readerMapperPlan = null!;
    private Func<fakeDbDataReader, HydrationEntity> _compiledGatewayMapper = null!;
    private Func<DbDataReader, HydrationEntity> _compiledDbReaderMapper = null!;
    private IReadOnlyDictionary<string, IColumnInfo> _columnMap = null!;
    private string[] _fieldNames = null!;
    private Type[] _fieldTypes = null!;
    private int _expectedChecksum;

    [Params(100, 1000, 5000)]
    public int RowCount { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _rows = BuildRows(RowCount);

        // Build both cached artifacts outside the measured path. Dapper's parser and
        // pengdows.crud's DataReaderMapper plan must not make the benchmark include cold setup.
        using (var dapperReader = CreateReader())
        {
            _dapperParser = dapperReader.GetRowParser<HydrationEntity>();
            _fieldNames = Enumerable.Range(0, dapperReader.FieldCount).Select(dapperReader.GetName).ToArray();
            _fieldTypes = Enumerable.Range(0, dapperReader.FieldCount).Select(dapperReader.GetFieldType).ToArray();
        }

        var tableInfo = new TypeMapRegistry().GetTableInfo<HydrationEntity>();
        _columnMap = tableInfo.Columns;
        using (var compiledReader = CreateReader())
        {
            _compiledGatewayMapper = CompiledMapperFactory<HydrationEntity>.Create(
                compiledReader,
                _columnMap,
                EnumParseFailureMode.Throw,
                _fieldNames,
                _fieldTypes);

            _compiledDbReaderMapper = CompiledMapperFactory<HydrationEntity>.Create<DbDataReader>(
                compiledReader,
                _columnMap,
                EnumParseFailureMode.Throw,
                _fieldNames,
                _fieldTypes);
        }

        using (var planReader = CreateReader())
        {
            _readerMapperPlan = DataReaderMapper.ResolveRowMapper<HydrationEntity>(planReader, MapperOptions.Default);
        }

        await using var pengdowsReader = CreateReader();
        var pengdowsRows = await DataReaderMapper.LoadAsync<HydrationEntity>(pengdowsReader, MapperOptions.Default);

        using var dapperHydrationReader = CreateReader();
        var dapperRows = new List<HydrationEntity>(RowCount);
        while (await dapperHydrationReader.ReadAsync())
        {
            dapperRows.Add(_dapperParser(dapperHydrationReader));
        }

        _expectedChecksum = Checksum(pengdowsRows);
        if (_expectedChecksum != Checksum(dapperRows))
        {
            throw new InvalidOperationException("The hydration benchmark arms produced different results.");
        }

        using (var plainReader = CreateReader())
        {
            _dapperPlainParser = plainReader.GetRowParser<PlainHydrationPoco>();
        }

        await using var pengdowsPlainReader = CreateReader();
        var pengdowsPlain = await DataReaderMapper.LoadAsync<PlainHydrationPoco>(pengdowsPlainReader, MapperOptions.Default);
        if (_expectedChecksum != Checksum(pengdowsPlain))
        {
            throw new InvalidOperationException("The plain-POCO hydration arms produced different results.");
        }

        using var compiledHydrationReader = CreateReader();
        if (!compiledHydrationReader.Read())
        {
            throw new InvalidOperationException("The compiled gateway reader had no rows.");
        }

        var compiledRow = _compiledGatewayMapper(compiledHydrationReader);
        if (compiledRow.Id != 1)
        {
            throw new InvalidOperationException("The compiled gateway mapper did not produce the expected row.");
        }
    }

    // ---- PublicApi: the call a person makes; mapping resolved on every call, lists not pre-sized ----

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PublicApi")]
    public async Task<int> Public_Pengdows()
    {
        await using var reader = CreateReader();
        var rows = await DataReaderMapper.LoadAsync<HydrationEntity>(reader, MapperOptions.Default);
        return rows.Count;
    }

    [Benchmark]
    [BenchmarkCategory("PublicApi")]
    public async Task<int> Public_Dapper()
    {
        await using var reader = CreateReader();
        var parser = reader.GetRowParser<HydrationEntity>();
        var rows = new List<HydrationEntity>();
        while (await reader.ReadAsync())
        {
            rows.Add(parser(reader));
        }

        return rows.Count;
    }

    // The same two calls over a type with no pengdows attributes: an arbitrary POCO and an arbitrary result set.
    [Benchmark]
    [BenchmarkCategory("PublicApi")]
    public async Task<int> Public_Pengdows_PlainPoco()
    {
        await using var reader = CreateReader();
        var rows = await DataReaderMapper.LoadAsync<PlainHydrationPoco>(reader, MapperOptions.Default);
        return rows.Count;
    }

    [Benchmark]
    [BenchmarkCategory("PublicApi")]
    public async Task<int> Public_Dapper_PlainPoco()
    {
        await using var reader = CreateReader();
        var parser = reader.GetRowParser<PlainHydrationPoco>();
        var rows = new List<PlainHydrationPoco>();
        while (await reader.ReadAsync())
        {
            rows.Add(parser(reader));
        }

        return rows.Count;
    }

    // ---- WarmMapper: already-resolved mappers, one loop shape, pre-sized list; only the delegate varies ----

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("WarmMapper")]
    public async Task<int> Warm_Dapper()
    {
        await using var reader = CreateReader();
        var rows = new List<HydrationEntity>(RowCount);
        while (await reader.ReadAsync())
        {
            rows.Add(_dapperParser(reader));
        }

        return rows.Count;
    }

    // pengdows.crud path 1: DataReaderMapper's resolved plan (arbitrary POCOs), resolved once in GlobalSetup.
    [Benchmark]
    [BenchmarkCategory("WarmMapper")]
    public async Task<int> Warm_Pengdows_ReaderMapperPlan()
    {
        await using DbDataReader reader = CreateReader();
        var rows = new List<HydrationEntity>(RowCount);
        while (await reader.ReadAsync())
        {
            rows.Add(_readerMapperPlan(reader));
        }

        return rows.Count;
    }

    // pengdows.crud path 2: CompiledMapperFactory (TableGateway entities), Func<DbDataReader, T> like Dapper's parser.
    [Benchmark]
    [BenchmarkCategory("WarmMapper")]
    public async Task<int> Warm_Pengdows_CompiledMapper()
    {
        await using DbDataReader reader = CreateReader();
        var rows = new List<HydrationEntity>(RowCount);
        while (await reader.ReadAsync())
        {
            rows.Add(_compiledDbReaderMapper(reader));
        }

        return rows.Count;
    }

    // A reference: the delegate is specialized to the fakeDb reader type, which the base DbDataReader type
    // does not allow the JIT to exploit. Not a competitor to Dapper.
    [Benchmark]
    [BenchmarkCategory("WarmMapper")]
    public async Task<int> Warm_Pengdows_CompiledMapperSpecialized()
    {
        await using var reader = CreateReader();
        var rows = new List<HydrationEntity>(RowCount);
        while (await reader.ReadAsync())
        {
            rows.Add(_compiledGatewayMapper(reader));
        }

        return rows.Count;
    }

    [Benchmark]
    [BenchmarkCategory("WarmMapper")]
    public async Task<int> Warm_Manual()
    {
        await using var reader = CreateReader();
        var rows = new List<HydrationEntity>(RowCount);
        while (await reader.ReadAsync())
        {
            rows.Add(new HydrationEntity
            {
                Id = reader.GetInt32(0),
                LongId = reader.GetInt64(1),
                Name = reader.GetString(2),
                Email = reader.GetString(3),
                Age = reader.GetInt32(4),
                Salary = reader.GetDecimal(5),
                IsActive = reader.GetBoolean(6),
                CreatedOn = reader.GetDateTime(7),
                CreatedOffset = reader.GetFieldValue<DateTimeOffset>(8),
                Score = reader.GetDouble(9),
                Ratio = reader.GetFloat(10),
                CorrelationId = reader.GetGuid(11),
                Payload = (byte[])reader.GetValue(12)
            });
        }

        return rows.Count;
    }

    // The cost of walking the reader at all, with no mapping.
    [Benchmark]
    [BenchmarkCategory("WarmMapper")]
    public async Task<int> Warm_ReadOnlyFloor()
    {
        await using var reader = CreateReader();
        var checksum = 0;
        while (await reader.ReadAsync())
        {
            checksum = unchecked(checksum * 31 + reader.GetInt32(0));
        }

        return checksum;
    }

    // ---- Resolution: finding the mapping for a reader with no rows (schema warm, nothing to hydrate) ----

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolution")]
    public async Task<int> Resolve_Pengdows()
    {
        await using var reader = CreateReader(Array.Empty<Dictionary<string, object>>());
        var rows = await DataReaderMapper.LoadAsync<HydrationEntity>(reader, MapperOptions.Default);
        return rows.Count;
    }

    [Benchmark]
    [BenchmarkCategory("Resolution")]
    public async Task<int> Resolve_Dapper()
    {
        await using var reader = CreateReader(Array.Empty<Dictionary<string, object>>());
        var parser = reader.GetRowParser<HydrationEntity>();
        return parser is null ? -1 : 0;
    }

    private fakeDbDataReader CreateReader()
    {
        return CreateReader(_rows);
    }

    private static fakeDbDataReader CreateReader(IReadOnlyList<Dictionary<string, object>> rows)
    {
        return new fakeDbDataReader(rows)
        {
            Columns = Columns
        };
    }

    private static List<Dictionary<string, object>> BuildRows(int rowCount)
    {
        var rows = new List<Dictionary<string, object>>(rowCount);
        for (var i = 1; i <= rowCount; i++)
        {
            rows.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = i,
                ["LongId"] = 10_000_000_000L + i,
                ["Name"] = $"Entity {i}",
                ["Email"] = $"entity{i}@example.com",
                ["Age"] = 20 + i % 50,
                ["Salary"] = 50_000m + i * 10m,
                ["IsActive"] = i % 2 == 0,
                ["CreatedOn"] = DateTime.UnixEpoch.AddMinutes(-i),
                ["CreatedOffset"] = new DateTimeOffset(DateTime.UnixEpoch.AddMinutes(-i), TimeSpan.Zero),
                ["Score"] = i * 1.25d,
                ["Ratio"] = i / 3.0f,
                ["CorrelationId"] = DeterministicGuid(i),
                ["Payload"] = BitConverter.GetBytes(i)
            });
        }

        return rows;
    }

    private static int Checksum(IReadOnlyList<HydrationEntity> rows)
    {
        var checksum = 0;
        foreach (var row in rows)
        {
            checksum = unchecked(checksum * 31 + row.Id);
            checksum = unchecked(checksum * 31 + (int)row.LongId);
            checksum = unchecked(checksum * 31 + row.Age);
            checksum = unchecked(checksum * 31 + (row.IsActive ? 1 : 0));
            checksum = unchecked(checksum * 31 + row.CorrelationId.GetHashCode());
            checksum = unchecked(checksum * 31 + row.Payload.Length);
        }

        return checksum;
    }

    private static int Checksum(IReadOnlyList<PlainHydrationPoco> rows)
    {
        var checksum = 0;
        foreach (var row in rows)
        {
            checksum = unchecked(checksum * 31 + row.Id);
            checksum = unchecked(checksum * 31 + (int)row.LongId);
            checksum = unchecked(checksum * 31 + row.Age);
            checksum = unchecked(checksum * 31 + (row.IsActive ? 1 : 0));
            checksum = unchecked(checksum * 31 + row.CorrelationId.GetHashCode());
            checksum = unchecked(checksum * 31 + row.Payload.Length);
        }

        return checksum;
    }

    private static Guid DeterministicGuid(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    [Table("hydration_benchmark")]
    public sealed class HydrationEntity
    {
        [Column("Id", System.Data.DbType.Int32)]
        [Id(false)]
        public int Id { get; set; }
        [Column("LongId", System.Data.DbType.Int64)]
        public long LongId { get; set; }
        [Column("Name", System.Data.DbType.String)]
        public string Name { get; set; } = string.Empty;
        [Column("Email", System.Data.DbType.String)]
        public string Email { get; set; } = string.Empty;
        [Column("Age", System.Data.DbType.Int32)]
        public int Age { get; set; }
        [Column("Salary", System.Data.DbType.Decimal)]
        public decimal Salary { get; set; }
        [Column("IsActive", System.Data.DbType.Boolean)]
        public bool IsActive { get; set; }
        [Column("CreatedOn", System.Data.DbType.DateTime)]
        public DateTime CreatedOn { get; set; }
        [Column("CreatedOffset", System.Data.DbType.DateTimeOffset)]
        public DateTimeOffset CreatedOffset { get; set; }
        [Column("Score", System.Data.DbType.Double)]
        public double Score { get; set; }
        [Column("Ratio", System.Data.DbType.Single)]
        public float Ratio { get; set; }
        [Column("CorrelationId", System.Data.DbType.Guid)]
        public Guid CorrelationId { get; set; }
        [Column("Payload", System.Data.DbType.Binary)]
        public byte[] Payload { get; set; } = [];
    }

    /// <summary>The same shape as <see cref="HydrationEntity"/> with no pengdows metadata at all.</summary>
    public sealed class PlainHydrationPoco
    {
        public int Id { get; set; }
        public long LongId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int Age { get; set; }
        public decimal Salary { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTimeOffset CreatedOffset { get; set; }
        public double Score { get; set; }
        public float Ratio { get; set; }
        public Guid CorrelationId { get; set; }
        public byte[] Payload { get; set; } = [];
    }
}
