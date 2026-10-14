using System.Data;
using System.Data.Common;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Dapper;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;

namespace CrudBenchmarks;

/// <summary>
/// Pure row-hydration comparison between pengdows.crud's DataReaderMapper and Dapper.
///
/// There is deliberately no connection, command, SQL execution, provider, network, pool,
/// transaction, or SQL-generation work in either benchmark arm. Both arms consume a fresh
/// fakeDb reader over the same prebuilt rows and the same declared schema. Mapping plans/parsers
/// are warmed before measurement. The benchmark therefore answers only: once an IDataReader
/// already exists, how much does each mapper cost to turn its rows into POCOs?
///
/// fakeDb intentionally has very cheap in-memory getters. That makes this a useful lower-bound
/// mapper-overhead measurement, but it is not a substitute for a provider benchmark: real driver
/// decoding can dominate the row cost, and production-provider results may have a different ratio.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
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

    [Benchmark(Baseline = true)]
    public async Task<int> Hydrate_Pengdows()
    {
        await using var reader = CreateReader();
        var rows = await DataReaderMapper.LoadAsync<HydrationEntity>(reader, MapperOptions.Default);
        return VerifyChecksum(rows);
    }

    [Benchmark]
    public async Task<int> Hydrate_Dapper()
    {
        await using var reader = CreateReader();
        var rows = new List<HydrationEntity>(RowCount);
        while (await reader.ReadAsync())
        {
            rows.Add(_dapperParser(reader));
        }

        return VerifyChecksum(rows);
    }

    [Benchmark]
    public async Task<int> Hydrate_CompiledGateway()
    {
        await using var reader = CreateReader();
        var rows = new List<HydrationEntity>(RowCount);
        while (await reader.ReadAsync())
        {
            rows.Add(_compiledGatewayMapper(reader));
        }

        return VerifyChecksum(rows);
    }

    [Benchmark]
    public async Task<int> Hydrate_CompiledLikeForLike()
    {
        await using DbDataReader reader = CreateReader();
        var rows = new List<HydrationEntity>(RowCount);
        while (await reader.ReadAsync())
        {
            rows.Add(_compiledDbReaderMapper(reader));
        }

        return VerifyChecksum(rows);
    }

    [Benchmark]
    public async Task<int> Hydrate_Manual()
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

        return VerifyChecksum(rows);
    }

    [Benchmark]
    public async Task<int> ReadOnly()
    {
        await using var reader = CreateReader();
        var checksum = 0;
        while (await reader.ReadAsync())
        {
            checksum = unchecked(checksum * 31 + reader.GetInt32(0));
        }

        return checksum;
    }

    [Benchmark]
    public async Task<int> Pengdows_Empty()
    {
        await using var reader = CreateReader(Array.Empty<Dictionary<string, object>>());
        var rows = await DataReaderMapper.LoadAsync<HydrationEntity>(reader, MapperOptions.Default);
        return rows.Count;
    }

    [Benchmark]
    public async Task<int> Dapper_Empty()
    {
        await using var reader = CreateReader(Array.Empty<Dictionary<string, object>>());
        var rows = new List<HydrationEntity>();
        while (await reader.ReadAsync())
        {
            rows.Add(_dapperParser(reader));
        }

        return rows.Count;
    }

    [Benchmark]
    public async Task<int> Manual_Empty()
    {
        await using var reader = CreateReader(Array.Empty<Dictionary<string, object>>());
        var rows = new List<HydrationEntity>();
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

    private static Guid DeterministicGuid(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    private int VerifyChecksum(IReadOnlyList<HydrationEntity> rows)
    {
        var checksum = Checksum(rows);
        if (checksum != _expectedChecksum)
        {
            throw new InvalidOperationException("The hydration benchmark produced an unexpected result.");
        }

        return checksum;
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
}
