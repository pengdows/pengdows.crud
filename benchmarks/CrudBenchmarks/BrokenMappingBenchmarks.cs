using System.Data;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using pengdows.crud;
using pengdows.crud.attributes;

namespace CrudBenchmarks;

/// <summary>
/// Proves thesis points #6 (Things BREAK in Dapper/EF) and #7 (No fixing).
///
/// When table and column names contain spaces or SQL keywords, pengdows.crud
/// handles them cleanly via [Column("First Name")] attributes. Dapper and EF,
/// when used WITHOUT manual workarounds (no MapDapperRow helper, no HasColumnName
/// mappings), silently return null/default values or throw exceptions.
///
/// Dapper: Uses QuerySingleOrDefaultAsync&lt;DapperWeirdEntity&gt;() directly —
///   columns with spaces don't map to C# properties, so fields come back as default.
///
/// EF: Uses convention-based mapping only (no HasColumnName) — columns with spaces
///   cannot be discovered, causing failures or silent data loss.
///
/// All Dapper/EF methods wrap operations in try/catch and log [BROKEN] to console.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class BrokenMappingBenchmarks : IDisposable
{
    // ============================================================================
    // SQL TEMPLATES
    // ============================================================================

    private const string InsertSqlTemplate = """
        INSERT INTO "Weird Table Names" ("First Name", "Last Name", "Email Address", "Phone Number", "select", "Is Active")
        VALUES ({firstName}, {lastName}, {emailAddress}, {phoneNumber}, {selectValue}, {isActive})
        """;

    private const string SelectSingleSqlTemplate = """
        SELECT "id", "First Name", "Last Name", "Email Address", "Phone Number", "select", "Is Active"
        FROM "Weird Table Names"
        WHERE "id" = {id}
        """;

    private const string SelectAllSqlTemplate = """
        SELECT "id", "First Name", "Last Name", "Email Address", "Phone Number", "select", "Is Active"
        FROM "Weird Table Names"
        """;

    private const string UpdateSqlTemplate = """
        UPDATE "Weird Table Names"
        SET "First Name" = {firstName}, "Last Name" = {lastName}, "Email Address" = {emailAddress},
            "Phone Number" = {phoneNumber}, "select" = {selectValue}, "Is Active" = {isActive}
        WHERE "id" = {id}
        """;

    private const string DeleteSqlTemplate = """
        DELETE FROM "Weird Table Names" WHERE "id" = {id}
        """;

    private const string FilteredQuerySqlTemplate = """
        SELECT "id", "First Name", "Last Name", "Email Address", "Phone Number", "select", "Is Active"
        FROM "Weird Table Names"
        WHERE "First Name" LIKE {firstName} AND "Is Active" = {isActive}
        """;

    private const string UpsertSqlTemplate = """
        INSERT INTO "Weird Table Names" ("id", "First Name", "Last Name", "Email Address", "Phone Number", "select", "Is Active")
        VALUES ({id}, {firstName}, {lastName}, {emailAddress}, {phoneNumber}, {selectValue}, {isActive})
        ON CONFLICT("id") DO UPDATE SET
            "First Name" = excluded."First Name",
            "Last Name" = excluded."Last Name",
            "Email Address" = excluded."Email Address",
            "Phone Number" = excluded."Phone Number",
            "select" = excluded."select",
            "Is Active" = excluded."Is Active"
        """;

    private const string AggregateCountSqlTemplate = """
        SELECT COUNT(*) FROM "Weird Table Names" WHERE "Is Active" = {isActive}
        """;

    private const string ReadWithKeywordSqlTemplate = """
        SELECT "id", "First Name", "Last Name", "Email Address", "Phone Number", "select", "Is Active"
        FROM "Weird Table Names"
        WHERE "select" = {selectValue}
        """;

    private const string FilterByKeywordSqlTemplate = """
        SELECT "id", "First Name", "Last Name", "Email Address", "Phone Number", "select", "Is Active"
        FROM "Weird Table Names"
        WHERE "select" > {selectValue}
        """;

    private const string UpdateKeywordSqlTemplate = """
        UPDATE "Weird Table Names"
        SET "select" = {selectValue}
        WHERE "id" = {id}
        """;

    private const string DeleteByKeywordSqlTemplate = """
        DELETE FROM "Weird Table Names" WHERE "select" = {selectValue}
        """;

    // ============================================================================
    // FIELDS
    // ============================================================================

    private DatabaseContext _pengdowsContext = null!;
    private TableGateway<WeirdEntity, int> _pengdowsHelper = null!;
    private SqliteConnection _dapperConnection = null!;
    private SqliteConnection _sentinelConnection = null!;
    private EfBrokenWeirdContext _efContext = null!;
    private TypeMapRegistry _typeMap = null!;
    private string _connectionString = null!;
    private int _deleteIdSeed = 10000;

    // ============================================================================
    // SETUP / CLEANUP
    // ============================================================================

    [GlobalSetup]
    public void Setup()
    {
        _connectionString = "Data Source=BrokenMapping;Mode=Memory;Cache=Shared";

        // Sentinel connection keeps in-memory DB alive
        _sentinelConnection = new SqliteConnection(_connectionString);
        _sentinelConnection.Open();

        // pengdows.crud
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<WeirdEntity>();
        _pengdowsContext = new DatabaseContext(_connectionString, SqliteFactory.Instance, _typeMap);
        _pengdowsHelper = new TableGateway<WeirdEntity, int>(_pengdowsContext);

        // Dapper — no special type maps, no custom mapping
        _dapperConnection = new SqliteConnection(_connectionString);
        _dapperConnection.Open();

        // EF — convention-based only, NO HasColumnName mappings
        var efOptions = new DbContextOptionsBuilder<EfBrokenWeirdContext>()
            .UseSqlite(_connectionString)
            .Options;
        _efContext = new EfBrokenWeirdContext(efOptions);

        CreateSchema();
        SeedData();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pengdowsContext?.Dispose();
        _dapperConnection?.Dispose();
        _efContext?.Dispose();
        _sentinelConnection?.Dispose();
    }

    private void CreateSchema()
    {
        var createSql = @"
            CREATE TABLE ""Weird Table Names"" (
                ""id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""First Name"" TEXT NOT NULL,
                ""Last Name"" TEXT NOT NULL,
                ""Email Address"" TEXT NOT NULL,
                ""Phone Number"" TEXT,
                ""select"" INTEGER NOT NULL,
                ""Is Active"" INTEGER NOT NULL
            )";

        using var container = _pengdowsContext.CreateSqlContainer(createSql);
        container.ExecuteNonQueryAsync().AsTask().Wait();
    }

    private void SeedData()
    {
        for (int i = 1; i <= 100; i++)
        {
            var entity = new WeirdEntity
            {
                FirstName = $"First{i}",
                LastName = $"Last{i}",
                EmailAddress = $"user{i}@example.com",
                PhoneNumber = $"555-{i:D4}",
                Select = i,
                IsActive = i % 2 == 0
            };
            _pengdowsHelper.CreateAsync(entity).Wait();
        }
    }

    // ============================================================================
    // CREATE BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<int> Create_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildInsertSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        BindInsertParameters(container, "NewFirst", "NewLast", "new@example.com", "555-NEW0", 200, true);
        return await container.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> Create_Dapper()
    {
        try
        {
            var sql = BuildInsertSql(param => $"@{param}");
            return await _dapperConnection.ExecuteAsync(sql, new
            {
                firstName = "NewFirst",
                lastName = "NewLast",
                emailAddress = "new@example.com",
                phoneNumber = "555-NEW0",
                selectValue = 200,
                isActive = 1
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] Dapper Create: {ex.Message}");
            return 0;
        }
    }

    [Benchmark]
    public async Task<int> Create_EntityFramework()
    {
        try
        {
            var sql = BuildInsertSql(param => $"@{param}");
            return await _efContext.Database.ExecuteSqlRawAsync(
                sql,
                new SqliteParameter("firstName", "NewFirst"),
                new SqliteParameter("lastName", "NewLast"),
                new SqliteParameter("emailAddress", "new@example.com"),
                new SqliteParameter("phoneNumber", "555-NEW0"),
                new SqliteParameter("selectValue", 200),
                new SqliteParameter("isActive", 1));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF Create: {ex.Message}");
            return 0;
        }
    }

    // ============================================================================
    // READ SINGLE BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<WeirdEntity?> ReadSingle_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildSelectSingleSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("id", DbType.Int32, 1);
        return await _pengdowsHelper.LoadSingleAsync(container);
    }

    [Benchmark]
    public async Task<DapperWeirdEntity?> ReadSingle_Dapper()
    {
        try
        {
            // No MapDapperRow helper — uses strongly-typed generic directly.
            // Columns with spaces ("First Name", "Last Name", etc.) won't map
            // to C# properties (FirstName, LastName), so they come back as default.
            var sql = BuildSelectSingleSql(param => $"@{param}");
            return await _dapperConnection.QuerySingleOrDefaultAsync<DapperWeirdEntity>(sql, new { id = 1 });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] Dapper ReadSingle: {ex.Message}");
            return null;
        }
    }

    [Benchmark]
    public async Task<EfBrokenWeirdEntity?> ReadSingle_EntityFramework()
    {
        try
        {
            // No HasColumnName mappings — EF convention mapping can't resolve
            // columns with spaces or SQL keyword column names.
            var sql = BuildSelectSingleSql(param => $"@{param}");
            return await _efContext.WeirdEntities
                .FromSqlRaw(sql, new SqliteParameter("id", 1))
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF ReadSingle: {ex.Message}");
            return null;
        }
    }

    // ============================================================================
    // READ LIST BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<List<WeirdEntity>> ReadList_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildFilteredQuerySql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("firstName", DbType.String, "First%");
        container.AddParameterWithValue("isActive", DbType.Boolean, true);
        return await _pengdowsHelper.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<DapperWeirdEntity>> ReadList_Dapper()
    {
        try
        {
            var sql = BuildFilteredQuerySql(param => $"@{param}");
            var rows = await _dapperConnection.QueryAsync<DapperWeirdEntity>(sql, new { firstName = "First%", isActive = 1 });
            return rows.ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] Dapper ReadList: {ex.Message}");
            return new List<DapperWeirdEntity>();
        }
    }

    [Benchmark]
    public async Task<List<EfBrokenWeirdEntity>> ReadList_EntityFramework()
    {
        try
        {
            var sql = BuildFilteredQuerySql(param => $"@{param}");
            return await _efContext.WeirdEntities
                .FromSqlRaw(
                    sql,
                    new SqliteParameter("firstName", "First%"),
                    new SqliteParameter("isActive", 1))
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF ReadList: {ex.Message}");
            return new List<EfBrokenWeirdEntity>();
        }
    }

    // ============================================================================
    // UPDATE BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<int> Update_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildUpdateSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("firstName", DbType.String, "UpdatedFirst");
        container.AddParameterWithValue("lastName", DbType.String, "UpdatedLast");
        container.AddParameterWithValue("emailAddress", DbType.String, "updated@example.com");
        container.AddParameterWithValue("phoneNumber", DbType.String, "555-UPDT");
        container.AddParameterWithValue("selectValue", DbType.Int32, 999);
        container.AddParameterWithValue("isActive", DbType.Boolean, false);
        container.AddParameterWithValue("id", DbType.Int32, 2);
        return await container.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> Update_Dapper()
    {
        try
        {
            var sql = BuildUpdateSql(param => $"@{param}");
            return await _dapperConnection.ExecuteAsync(sql, new
            {
                firstName = "UpdatedFirst",
                lastName = "UpdatedLast",
                emailAddress = "updated@example.com",
                phoneNumber = "555-UPDT",
                selectValue = 999,
                isActive = 0,
                id = 2
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] Dapper Update: {ex.Message}");
            return 0;
        }
    }

    [Benchmark]
    public async Task<int> Update_EntityFramework()
    {
        try
        {
            var sql = BuildUpdateSql(param => $"@{param}");
            return await _efContext.Database.ExecuteSqlRawAsync(
                sql,
                new SqliteParameter("firstName", "UpdatedFirst"),
                new SqliteParameter("lastName", "UpdatedLast"),
                new SqliteParameter("emailAddress", "updated@example.com"),
                new SqliteParameter("phoneNumber", "555-UPDT"),
                new SqliteParameter("selectValue", 999),
                new SqliteParameter("isActive", 0),
                new SqliteParameter("id", 2));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF Update: {ex.Message}");
            return 0;
        }
    }

    // ============================================================================
    // DELETE BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<int> Delete_Pengdows()
    {
        var id = Interlocked.Increment(ref _deleteIdSeed);
        await SeedOneRow(id);

        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildDeleteSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("id", DbType.Int32, id);
        return await container.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> Delete_Dapper()
    {
        var id = Interlocked.Increment(ref _deleteIdSeed);
        await SeedOneRow(id);

        try
        {
            var sql = BuildDeleteSql(param => $"@{param}");
            return await _dapperConnection.ExecuteAsync(sql, new { id });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] Dapper Delete: {ex.Message}");
            return 0;
        }
    }

    [Benchmark]
    public async Task<int> Delete_EntityFramework()
    {
        var id = Interlocked.Increment(ref _deleteIdSeed);
        await SeedOneRow(id);

        try
        {
            var sql = BuildDeleteSql(param => $"@{param}");
            return await _efContext.Database.ExecuteSqlRawAsync(sql, new SqliteParameter("id", id));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF Delete: {ex.Message}");
            return 0;
        }
    }

    // ============================================================================
    // FILTERED QUERY BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<List<WeirdEntity>> FilteredQuery_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildFilteredQuerySql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("firstName", DbType.String, "First1%");
        container.AddParameterWithValue("isActive", DbType.Boolean, true);
        return await _pengdowsHelper.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<DapperWeirdEntity>> FilteredQuery_Dapper()
    {
        try
        {
            var sql = BuildFilteredQuerySql(param => $"@{param}");
            var rows = await _dapperConnection.QueryAsync<DapperWeirdEntity>(sql, new { firstName = "First1%", isActive = 1 });
            return rows.ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] Dapper FilteredQuery: {ex.Message}");
            return new List<DapperWeirdEntity>();
        }
    }

    [Benchmark]
    public async Task<List<EfBrokenWeirdEntity>> FilteredQuery_EntityFramework()
    {
        try
        {
            var sql = BuildFilteredQuerySql(param => $"@{param}");
            return await _efContext.WeirdEntities
                .FromSqlRaw(
                    sql,
                    new SqliteParameter("firstName", "First1%"),
                    new SqliteParameter("isActive", 1))
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF FilteredQuery: {ex.Message}");
            return new List<EfBrokenWeirdEntity>();
        }
    }

    // ============================================================================
    // UPSERT BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<int> Upsert_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildUpsertSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        BindUpsertParameters(container, 1, "Upserted", "Person", "upsert@example.com", "555-UPSR", 888, true);
        return await container.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> Upsert_Dapper()
    {
        try
        {
            var sql = BuildUpsertSql(param => $"@{param}");
            return await _dapperConnection.ExecuteAsync(sql, new
            {
                id = 1,
                firstName = "Upserted",
                lastName = "Person",
                emailAddress = "upsert@example.com",
                phoneNumber = "555-UPSR",
                selectValue = 888,
                isActive = 1
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] Dapper Upsert: {ex.Message}");
            return 0;
        }
    }

    [Benchmark]
    public async Task<int> Upsert_EntityFramework()
    {
        try
        {
            var sql = BuildUpsertSql(param => $"@{param}");
            return await _efContext.Database.ExecuteSqlRawAsync(
                sql,
                new SqliteParameter("id", 1),
                new SqliteParameter("firstName", "Upserted"),
                new SqliteParameter("lastName", "Person"),
                new SqliteParameter("emailAddress", "upsert@example.com"),
                new SqliteParameter("phoneNumber", "555-UPSR"),
                new SqliteParameter("selectValue", 888),
                new SqliteParameter("isActive", 1));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF Upsert: {ex.Message}");
            return 0;
        }
    }

    // ============================================================================
    // AGGREGATE COUNT BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<int> AggregateCount_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildAggregateCountSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("isActive", DbType.Boolean, true);
        var result = await container.ExecuteScalarAsync<long>();
        return (int)result;
    }

    [Benchmark]
    public async Task<int> AggregateCount_Dapper()
    {
        try
        {
            var sql = BuildAggregateCountSql(param => $"@{param}");
            return await _dapperConnection.ExecuteScalarAsync<int>(sql, new { isActive = 1 });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] Dapper AggregateCount: {ex.Message}");
            return 0;
        }
    }

    [Benchmark]
    public async Task<int> AggregateCount_EntityFramework()
    {
        try
        {
            var sql = BuildAggregateCountSql(param => $"@{param}");
            // EF doesn't support ExecuteScalarAsync directly from raw SQL easily,
            // so we use a connection-level approach via the context
            var conn = _efContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var p = cmd.CreateParameter();
            p.ParameterName = "isActive";
            p.Value = 1;
            cmd.Parameters.Add(p);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF AggregateCount: {ex.Message}");
            return 0;
        }
    }

    // ============================================================================
    // READ WITH KEYWORD COLUMN BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<List<WeirdEntity>> ReadWithKeyword_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildReadWithKeywordSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("selectValue", DbType.Int32, 42);
        return await _pengdowsHelper.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<DapperWeirdEntity>> ReadWithKeyword_Dapper()
    {
        try
        {
            var sql = BuildReadWithKeywordSql(param => $"@{param}");
            var rows = await _dapperConnection.QueryAsync<DapperWeirdEntity>(sql, new { selectValue = 42 });
            return rows.ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] Dapper ReadWithKeyword: {ex.Message}");
            return new List<DapperWeirdEntity>();
        }
    }

    [Benchmark]
    public async Task<List<EfBrokenWeirdEntity>> ReadWithKeyword_EntityFramework()
    {
        try
        {
            var sql = BuildReadWithKeywordSql(param => $"@{param}");
            return await _efContext.WeirdEntities
                .FromSqlRaw(sql, new SqliteParameter("selectValue", 42))
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF ReadWithKeyword: {ex.Message}");
            return new List<EfBrokenWeirdEntity>();
        }
    }

    // ============================================================================
    // FILTER BY KEYWORD COLUMN BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<List<WeirdEntity>> FilterByKeyword_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildFilterByKeywordSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("selectValue", DbType.Int32, 50);
        return await _pengdowsHelper.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<DapperWeirdEntity>> FilterByKeyword_Dapper()
    {
        try
        {
            var sql = BuildFilterByKeywordSql(param => $"@{param}");
            var rows = await _dapperConnection.QueryAsync<DapperWeirdEntity>(sql, new { selectValue = 50 });
            return rows.ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] Dapper FilterByKeyword: {ex.Message}");
            return new List<DapperWeirdEntity>();
        }
    }

    [Benchmark]
    public async Task<List<EfBrokenWeirdEntity>> FilterByKeyword_EntityFramework()
    {
        try
        {
            var sql = BuildFilterByKeywordSql(param => $"@{param}");
            return await _efContext.WeirdEntities
                .FromSqlRaw(sql, new SqliteParameter("selectValue", 50))
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF FilterByKeyword: {ex.Message}");
            return new List<EfBrokenWeirdEntity>();
        }
    }

    // ============================================================================
    // UPDATE KEYWORD COLUMN BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<int> UpdateKeyword_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildUpdateKeywordSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("selectValue", DbType.Int32, 12345);
        container.AddParameterWithValue("id", DbType.Int32, 3);
        return await container.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> UpdateKeyword_Dapper()
    {
        try
        {
            var sql = BuildUpdateKeywordSql(param => $"@{param}");
            return await _dapperConnection.ExecuteAsync(sql, new { selectValue = 12345, id = 3 });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] Dapper UpdateKeyword: {ex.Message}");
            return 0;
        }
    }

    [Benchmark]
    public async Task<int> UpdateKeyword_EntityFramework()
    {
        try
        {
            var sql = BuildUpdateKeywordSql(param => $"@{param}");
            return await _efContext.Database.ExecuteSqlRawAsync(
                sql,
                new SqliteParameter("selectValue", 12345),
                new SqliteParameter("id", 3));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF UpdateKeyword: {ex.Message}");
            return 0;
        }
    }

    // ============================================================================
    // BATCH CREATE BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<int> BatchCreate_Pengdows()
    {
        var count = 0;
        for (int i = 0; i < 10; i++)
        {
            await using var container = _pengdowsContext.CreateSqlContainer();
            var sql = BuildInsertSql(param => container.MakeParameterName(param));
            container.Query.Append(sql);
            BindInsertParameters(container, $"Batch{i}", $"User{i}", $"batch{i}@example.com", $"555-B{i:D3}0", 300 + i, true);
            await container.ExecuteNonQueryAsync();
            count++;
        }
        return count;
    }

    [Benchmark]
    public async Task<int> BatchCreate_Dapper()
    {
        var count = 0;
        for (int i = 0; i < 10; i++)
        {
            try
            {
                var sql = BuildInsertSql(param => $"@{param}");
                await _dapperConnection.ExecuteAsync(sql, new
                {
                    firstName = $"Batch{i}",
                    lastName = $"User{i}",
                    emailAddress = $"batch{i}@example.com",
                    phoneNumber = $"555-B{i:D3}0",
                    selectValue = 300 + i,
                    isActive = 1
                });
                count++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BROKEN] Dapper BatchCreate[{i}]: {ex.Message}");
            }
        }
        return count;
    }

    [Benchmark]
    public async Task<int> BatchCreate_EntityFramework()
    {
        var count = 0;
        for (int i = 0; i < 10; i++)
        {
            try
            {
                var sql = BuildInsertSql(param => $"@{param}");
                await _efContext.Database.ExecuteSqlRawAsync(
                    sql,
                    new SqliteParameter("firstName", $"Batch{i}"),
                    new SqliteParameter("lastName", $"User{i}"),
                    new SqliteParameter("emailAddress", $"batch{i}@example.com"),
                    new SqliteParameter("phoneNumber", $"555-B{i:D3}0"),
                    new SqliteParameter("selectValue", 300 + i),
                    new SqliteParameter("isActive", 1));
                count++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BROKEN] EF BatchCreate[{i}]: {ex.Message}");
            }
        }
        return count;
    }

    // ============================================================================
    // BATCH READ BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<List<WeirdEntity>> BatchRead_Pengdows()
    {
        var results = new List<WeirdEntity>();
        for (int id = 1; id <= 10; id++)
        {
            await using var container = _pengdowsContext.CreateSqlContainer();
            var sql = BuildSelectSingleSql(param => container.MakeParameterName(param));
            container.Query.Append(sql);
            container.AddParameterWithValue("id", DbType.Int32, id);
            var entity = await _pengdowsHelper.LoadSingleAsync(container);
            if (entity != null) results.Add(entity);
        }
        return results;
    }

    [Benchmark]
    public async Task<List<DapperWeirdEntity>> BatchRead_Dapper()
    {
        var results = new List<DapperWeirdEntity>();
        for (int id = 1; id <= 10; id++)
        {
            try
            {
                var sql = BuildSelectSingleSql(param => $"@{param}");
                var entity = await _dapperConnection.QuerySingleOrDefaultAsync<DapperWeirdEntity>(sql, new { id });
                if (entity != null) results.Add(entity);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BROKEN] Dapper BatchRead[{id}]: {ex.Message}");
            }
        }
        return results;
    }

    [Benchmark]
    public async Task<List<EfBrokenWeirdEntity>> BatchRead_EntityFramework()
    {
        var results = new List<EfBrokenWeirdEntity>();
        for (int id = 1; id <= 10; id++)
        {
            try
            {
                var sql = BuildSelectSingleSql(param => $"@{param}");
                var entity = await _efContext.WeirdEntities
                    .FromSqlRaw(sql, new SqliteParameter("id", id))
                    .AsNoTracking()
                    .FirstOrDefaultAsync();
                if (entity != null) results.Add(entity);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BROKEN] EF BatchRead[{id}]: {ex.Message}");
            }
        }
        return results;
    }

    // ============================================================================
    // READ ALL BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<List<WeirdEntity>> ReadAll_Pengdows()
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        container.Query.Append(SelectAllSqlTemplate);
        return await _pengdowsHelper.LoadListAsync(container);
    }

    [Benchmark]
    public async Task<List<DapperWeirdEntity>> ReadAll_Dapper()
    {
        try
        {
            var rows = await _dapperConnection.QueryAsync<DapperWeirdEntity>(SelectAllSqlTemplate);
            return rows.ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] Dapper ReadAll: {ex.Message}");
            return new List<DapperWeirdEntity>();
        }
    }

    [Benchmark]
    public async Task<List<EfBrokenWeirdEntity>> ReadAll_EntityFramework()
    {
        try
        {
            return await _efContext.WeirdEntities
                .FromSqlRaw(SelectAllSqlTemplate)
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF ReadAll: {ex.Message}");
            return new List<EfBrokenWeirdEntity>();
        }
    }

    // ============================================================================
    // DELETE BY KEYWORD COLUMN BENCHMARKS
    // ============================================================================

    [Benchmark]
    public async Task<int> DeleteByKeyword_Pengdows()
    {
        // Seed a row with a known select value for deletion
        var id = Interlocked.Increment(ref _deleteIdSeed);
        await SeedOneRowWithSelect(id, 77777);

        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = BuildDeleteByKeywordSql(param => container.MakeParameterName(param));
        container.Query.Append(sql);
        container.AddParameterWithValue("selectValue", DbType.Int32, 77777);
        return await container.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public async Task<int> DeleteByKeyword_Dapper()
    {
        var id = Interlocked.Increment(ref _deleteIdSeed);
        await SeedOneRowWithSelect(id, 88888);

        try
        {
            var sql = BuildDeleteByKeywordSql(param => $"@{param}");
            return await _dapperConnection.ExecuteAsync(sql, new { selectValue = 88888 });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] Dapper DeleteByKeyword: {ex.Message}");
            return 0;
        }
    }

    [Benchmark]
    public async Task<int> DeleteByKeyword_EntityFramework()
    {
        var id = Interlocked.Increment(ref _deleteIdSeed);
        await SeedOneRowWithSelect(id, 99999);

        try
        {
            var sql = BuildDeleteByKeywordSql(param => $"@{param}");
            return await _efContext.Database.ExecuteSqlRawAsync(sql, new SqliteParameter("selectValue", 99999));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BROKEN] EF DeleteByKeyword: {ex.Message}");
            return 0;
        }
    }

    // ============================================================================
    // HELPER: Seed a single row (used by Delete benchmarks)
    // ============================================================================

    private async Task SeedOneRow(int id)
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = $"""
            INSERT INTO "Weird Table Names" ("id", "First Name", "Last Name", "Email Address", "Phone Number", "select", "Is Active")
            VALUES ({container.MakeParameterName("id")}, {container.MakeParameterName("firstName")}, {container.MakeParameterName("lastName")},
                    {container.MakeParameterName("emailAddress")}, {container.MakeParameterName("phoneNumber")},
                    {container.MakeParameterName("selectValue")}, {container.MakeParameterName("isActive")})
            """;
        container.Query.Append(sql);
        container.AddParameterWithValue("id", DbType.Int32, id);
        container.AddParameterWithValue("firstName", DbType.String, "ToDelete");
        container.AddParameterWithValue("lastName", DbType.String, "Row");
        container.AddParameterWithValue("emailAddress", DbType.String, "delete@example.com");
        container.AddParameterWithValue("phoneNumber", DbType.String, "555-DEL0");
        container.AddParameterWithValue("selectValue", DbType.Int32, 0);
        container.AddParameterWithValue("isActive", DbType.Boolean, false);
        await container.ExecuteNonQueryAsync();
    }

    private async Task SeedOneRowWithSelect(int id, int selectValue)
    {
        await using var container = _pengdowsContext.CreateSqlContainer();
        var sql = $"""
            INSERT INTO "Weird Table Names" ("id", "First Name", "Last Name", "Email Address", "Phone Number", "select", "Is Active")
            VALUES ({container.MakeParameterName("id")}, {container.MakeParameterName("firstName")}, {container.MakeParameterName("lastName")},
                    {container.MakeParameterName("emailAddress")}, {container.MakeParameterName("phoneNumber")},
                    {container.MakeParameterName("selectValue")}, {container.MakeParameterName("isActive")})
            """;
        container.Query.Append(sql);
        container.AddParameterWithValue("id", DbType.Int32, id);
        container.AddParameterWithValue("firstName", DbType.String, "KeywordDel");
        container.AddParameterWithValue("lastName", DbType.String, "Row");
        container.AddParameterWithValue("emailAddress", DbType.String, "kwdel@example.com");
        container.AddParameterWithValue("phoneNumber", DbType.String, "555-KWD0");
        container.AddParameterWithValue("selectValue", DbType.Int32, selectValue);
        container.AddParameterWithValue("isActive", DbType.Boolean, false);
        await container.ExecuteNonQueryAsync();
    }

    // ============================================================================
    // SQL BUILDER HELPERS
    // ============================================================================

    private static string BuildInsertSql(Func<string, string> param)
    {
        return InsertSqlTemplate
            .Replace("{firstName}", param("firstName"))
            .Replace("{lastName}", param("lastName"))
            .Replace("{emailAddress}", param("emailAddress"))
            .Replace("{phoneNumber}", param("phoneNumber"))
            .Replace("{selectValue}", param("selectValue"))
            .Replace("{isActive}", param("isActive"));
    }

    private static string BuildSelectSingleSql(Func<string, string> param)
    {
        return SelectSingleSqlTemplate.Replace("{id}", param("id"));
    }

    private static string BuildUpdateSql(Func<string, string> param)
    {
        return UpdateSqlTemplate
            .Replace("{firstName}", param("firstName"))
            .Replace("{lastName}", param("lastName"))
            .Replace("{emailAddress}", param("emailAddress"))
            .Replace("{phoneNumber}", param("phoneNumber"))
            .Replace("{selectValue}", param("selectValue"))
            .Replace("{isActive}", param("isActive"))
            .Replace("{id}", param("id"));
    }

    private static string BuildDeleteSql(Func<string, string> param)
    {
        return DeleteSqlTemplate.Replace("{id}", param("id"));
    }

    private static string BuildFilteredQuerySql(Func<string, string> param)
    {
        return FilteredQuerySqlTemplate
            .Replace("{firstName}", param("firstName"))
            .Replace("{isActive}", param("isActive"));
    }

    private static string BuildUpsertSql(Func<string, string> param)
    {
        return UpsertSqlTemplate
            .Replace("{id}", param("id"))
            .Replace("{firstName}", param("firstName"))
            .Replace("{lastName}", param("lastName"))
            .Replace("{emailAddress}", param("emailAddress"))
            .Replace("{phoneNumber}", param("phoneNumber"))
            .Replace("{selectValue}", param("selectValue"))
            .Replace("{isActive}", param("isActive"));
    }

    private static string BuildAggregateCountSql(Func<string, string> param)
    {
        return AggregateCountSqlTemplate.Replace("{isActive}", param("isActive"));
    }

    private static string BuildReadWithKeywordSql(Func<string, string> param)
    {
        return ReadWithKeywordSqlTemplate.Replace("{selectValue}", param("selectValue"));
    }

    private static string BuildFilterByKeywordSql(Func<string, string> param)
    {
        return FilterByKeywordSqlTemplate.Replace("{selectValue}", param("selectValue"));
    }

    private static string BuildUpdateKeywordSql(Func<string, string> param)
    {
        return UpdateKeywordSqlTemplate
            .Replace("{selectValue}", param("selectValue"))
            .Replace("{id}", param("id"));
    }

    private static string BuildDeleteByKeywordSql(Func<string, string> param)
    {
        return DeleteByKeywordSqlTemplate.Replace("{selectValue}", param("selectValue"));
    }

    private static void BindInsertParameters(ISqlContainer container, string firstName, string lastName,
        string emailAddress, string phoneNumber, int selectValue, bool isActive)
    {
        container.AddParameterWithValue("firstName", DbType.String, firstName);
        container.AddParameterWithValue("lastName", DbType.String, lastName);
        container.AddParameterWithValue("emailAddress", DbType.String, emailAddress);
        container.AddParameterWithValue("phoneNumber", DbType.String, phoneNumber);
        container.AddParameterWithValue("selectValue", DbType.Int32, selectValue);
        container.AddParameterWithValue("isActive", DbType.Boolean, isActive);
    }

    private static void BindUpsertParameters(ISqlContainer container, int id, string firstName, string lastName,
        string emailAddress, string phoneNumber, int selectValue, bool isActive)
    {
        container.AddParameterWithValue("id", DbType.Int32, id);
        BindInsertParameters(container, firstName, lastName, emailAddress, phoneNumber, selectValue, isActive);
    }

    public void Dispose()
    {
        Cleanup();
    }

    // ============================================================================
    // ENTITIES
    // ============================================================================

    /// <summary>
    /// pengdows.crud entity — [Column] attributes handle the weird names cleanly.
    /// </summary>
    [Table("Weird Table Names")]
    public class WeirdEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("First Name", DbType.String)]
        public string FirstName { get; set; } = string.Empty;

        [Column("Last Name", DbType.String)]
        public string LastName { get; set; } = string.Empty;

        [Column("Email Address", DbType.String)]
        public string EmailAddress { get; set; } = string.Empty;

        [Column("Phone Number", DbType.String)]
        public string? PhoneNumber { get; set; }

        [Column("select", DbType.Int32)]
        public int Select { get; set; }

        [Column("Is Active", DbType.Boolean)]
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Dapper entity — NO special mapping. Property names don't match column names
    /// with spaces, so Dapper's automatic mapping silently returns default values.
    /// </summary>
    public class DapperWeirdEntity
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string EmailAddress { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public int Select { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// EF entity — NO HasColumnName mappings. Convention-based mapping cannot
    /// discover columns with spaces or SQL keyword names.
    /// </summary>
    public class EfBrokenWeirdEntity
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string EmailAddress { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public int Select { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// EF DbContext — convention-based only. Only ToTable and HasKey are configured.
    /// NO HasColumnName mappings — this is the entire point of the benchmark.
    /// </summary>
    public class EfBrokenWeirdContext : DbContext
    {
        public EfBrokenWeirdContext(DbContextOptions<EfBrokenWeirdContext> options) : base(options) { }

        public DbSet<EfBrokenWeirdEntity> WeirdEntities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EfBrokenWeirdEntity>(entity =>
            {
                entity.ToTable("Weird Table Names");
                entity.HasKey(e => e.Id);
            });
        }
    }
}
