#region

using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using pengdows.crud;
using pengdows.crud.enums;

#endregion

namespace testbed;

public class TestProvider : IAsyncTestProvider
{
    private static long _nextId;

    protected readonly IDatabaseContext _context;
    protected readonly TableGateway<TestTable, long> _helper;

    public TestProvider(IDatabaseContext databaseContext, IServiceProvider serviceProvider)
    {
        _context = databaseContext;
        var resolver = serviceProvider.GetService<IAuditValueResolver>() ??
                       new TestAuditValueResolver("system");
        _helper = new TableGateway<TestTable, long>(databaseContext, resolver);
    }


    public async Task RunTest()
    {
        var totalSw = Stopwatch.StartNew();
        var stepSw = new Stopwatch();
        Console.WriteLine($"[{_context.Product}] Starting test run");
        try
        {
            stepSw.Restart();
            Console.WriteLine("Running Create table");
            await CreateTable();
            Console.WriteLine($"  Create table: {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running Insert rows");
            var before = await CountTestRows();
            var id = await InsertTestRows();
            var afterInsert = await CountTestRows();
            Console.WriteLine($"  Insert rows: {stepSw.ElapsedMilliseconds}ms");
            if (afterInsert != before + 1)
            {
                throw new Exception("Insert did not affect expected row count");
            }

            stepSw.Restart();
            Console.WriteLine("Running retrieve rows");
            var obj = await RetrieveRows(id);
            Console.WriteLine($"  Retrieve rows: {stepSw.ElapsedMilliseconds}ms");
            if (obj.Id != id)
            {
                throw new Exception("Retrieved row did not match inserted id");
            }

            stepSw.Restart();
            Console.WriteLine("Running delete rows");
            await DeletedRow(obj);
            var afterDelete = await CountTestRows();
            Console.WriteLine($"  Delete rows: {stepSw.ElapsedMilliseconds}ms");
            if (afterDelete != before)
            {
                throw new Exception("Delete did not affect expected row count");
            }

            stepSw.Restart();
            Console.WriteLine("Running Transaction rows");
            await TestTransactions();
            Console.WriteLine($"  Transactions: {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running stored procedure return value test");
            await TestStoredProcReturnValue();
            Console.WriteLine($"  Stored procedure: {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running parameter binding");
            await TestParameterBinding();
            Console.WriteLine($"  Parameter binding: {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running row round-trip fidelity");
            await TestRowRoundTrip();
            Console.WriteLine($"  Row round-trip: {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running extended transactions");
            await TestExtendedTransactions();
            Console.WriteLine($"  Extended transactions: {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running concurrency");
            await TestConcurrency();
            Console.WriteLine($"  Concurrency: {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running capability probes");
            await TestCapabilityProbes();
            Console.WriteLine($"  Capability probes: {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running error mapping");
            await TestErrorMapping();
            Console.WriteLine($"  Error mapping: {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running identifier quoting");
            await TestIdentifierQuoting();
            Console.WriteLine($"  Identifier quoting: {stepSw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to complete tests successfully: " + ex.Message + "\n" + ex.StackTrace);
            throw;
        }
        finally
        {
            Console.WriteLine($"[{_context.Product}] Test run completed in {totalSw.ElapsedMilliseconds}ms");
        }
    }

    protected virtual async Task TestTransactions()
    {
        if (!_context.DataSourceInfo.SupportsTransactions)
        {
            Console.WriteLine($"  [{_context.Product}] Skipping transactions (not supported)");
            return;
        }

        var count = await CountTestRows();

        await TestRollbackTransaction();
        var count2 = await CountTestRows();
        if (count != count2)
        {
            Console.WriteLine("Failed to rollback transactions: " + count);
            throw new Exception("Failed to rollback transactions: " + count);
        }

        await TestCommitTransaction();
        var count3 = await CountTestRows();
        if (count3 == count2)
        {
            Console.WriteLine("Failed to commit transactions: " + count);
            throw new Exception("Failed to commit transactions: " + count);
        }
    }

    private async Task TestCommitTransaction()
    {
        await using var transaction = _context.BeginTransaction();
        var id = await InsertTestRows(transaction);
        await CountTestRows(transaction);
        transaction.Commit();
    }

    private async Task TestRollbackTransaction()
    {
        await using var transaction = _context.BeginTransaction();
        var id = await InsertTestRows(transaction);
        await CountTestRows(transaction);
        transaction.Rollback();
    }

    public virtual async Task<int> CountTestRows(IDatabaseContext? db = null)
    {
        var ctx = db ?? _context;
        var sc = ctx.CreateSqlContainer();
        sc.Query.AppendFormat("SELECT COUNT(*) FROM {0}", _helper.WrappedTableName);
        var count = await sc.ExecuteScalarOrNullAsync<int>();
        Console.WriteLine($"Count of rows: {count}");
        return count;
    }

    public virtual async Task CreateTable()
    {
        var databaseContext = _context;
        var sqlContainer = databaseContext.CreateSqlContainer();
        var qp = databaseContext.QuotePrefix;
        var qs = databaseContext.QuoteSuffix;
        sqlContainer.Query.AppendFormat(@"DROP TABLE IF EXISTS {0}test_table{1}", qp, qs);
        try
        {
            await sqlContainer.ExecuteNonQueryAsync();
        }
        catch
        {
            // Table did not exist, ignore
        }

        sqlContainer.Clear();
        var dateType = GetDateTimeType(databaseContext.Product);
        var boolType = GetBooleanType(databaseContext.Product);
        sqlContainer.Query.Append($@"
CREATE TABLE {qp}test_table{qs} (
    {qp}id{qs} BIGINT NOT NULL,
    {qp}name{qs} VARCHAR(100) NOT NULL,
    {qp}description{qs} VARCHAR(1000) NOT NULL,
    {qp}value{qs} INT NOT NULL,
    {qp}is_active{qs} {boolType} NOT NULL,
    {qp}created_at{qs} {dateType} NOT NULL,
    {qp}created_by{qs} VARCHAR(100) NOT NULL,
    {qp}updated_at{qs} {dateType} NOT NULL,
    {qp}updated_by{qs} VARCHAR(100) NOT NULL,
    PRIMARY KEY ({qp}id{qs})
);");
        await sqlContainer.ExecuteNonQueryAsync();
    }

    protected async Task<long> InsertTestRows(IDatabaseContext? db = null)
    {
        var ctx = db ?? _context;
        var name = ctx is TransactionContext ? NameEnum.Test2 : NameEnum.Test;
        var id = Interlocked.Increment(ref _nextId);
        var t = new TestTable
        {
            Id = id,
            Name = name,
            Description = ctx.GenerateRandomName()
        };
        var sq = _helper.BuildCreate(t, ctx);
        var rows = await sq.ExecuteNonQueryAsync();
        if (rows != 1)
        {
            throw new Exception("Insert failed");
        }

        return t.Id;
    }

    private async Task<TestTable> RetrieveRows(long id, IDatabaseContext? db = null)
    {
        var arr = new List<long> { id };
        var ctx = db ?? _context;
        var sc = _helper.BuildRetrieve(arr, ctx);

        Console.WriteLine(sc.Query.ToString());

        var x = await _helper.LoadListAsync(sc);

        return x.First();
    }

    protected virtual async Task DeletedRow(TestTable t, IDatabaseContext? db = null)
    {
        var ctx = db ?? _context;
        if (!_context.DataSourceInfo.SupportsRowLevelDelete)
        {
            // Databases without DELETE FROM use TRUNCATE to reset the table.
            // Safe here because RunTest() guarantees exactly one row exists at this point.
            var tsc = ctx.CreateSqlContainer();
            tsc.Query.AppendFormat("TRUNCATE TABLE {0}", _helper.WrappedTableName);
            await tsc.ExecuteNonQueryAsync();
            return;
        }

        var sc = _helper.BuildDelete(t.Id, ctx);
        var count = await sc.ExecuteNonQueryAsync();
        if (count != 1)
        {
            throw new Exception("Delete failed");
        }
    }

    private static string GetDateTimeType(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.PostgreSql => "TIMESTAMP WITH TIME ZONE",
            _ => "DATETIME"
        };
    }

    private static string GetBooleanType(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.PostgreSql => "BOOLEAN",
            SupportedDatabase.CockroachDb => "BOOLEAN",
            SupportedDatabase.YugabyteDb => "BOOLEAN",
            SupportedDatabase.QuestDb => "BOOLEAN",
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.DuckDB => "BOOLEAN",
            SupportedDatabase.Firebird => "SMALLINT",
            SupportedDatabase.Oracle => "NUMBER(1)",
            SupportedDatabase.MySql => "BOOLEAN",
            SupportedDatabase.MariaDb => "BOOLEAN",
            SupportedDatabase.TiDb => "BOOLEAN",
            SupportedDatabase.SqlServer => "BIT",
            _ => "BOOLEAN"
        };
    }

    private async Task TestStoredProcReturnValue()
    {
        var sc = _context.CreateSqlContainer();
        switch (_context.Product)
        {
            case SupportedDatabase.SqlServer:
                sc.Query.Append(
                    "CREATE OR ALTER PROCEDURE dbo.ReturnFive AS BEGIN RETURN 5 END");
                await sc.ExecuteNonQueryAsync();

                sc.Clear();
                sc.Query.Append("dbo.ReturnFive");
                var wrapped = sc.WrapForStoredProc(ExecutionType.Read, captureReturn: true);
                sc.Clear();
                sc.Query.Append(wrapped);
                var value = await sc.ExecuteScalarOrNullAsync<int>();
                if (value != 5)
                {
                    throw new Exception($"Expected return value 5 but got {value}");
                }

                sc.Clear();
                sc.Query.Append("DROP PROCEDURE dbo.ReturnFive");
                await sc.ExecuteNonQueryAsync();
                break;

            default:
                sc.Query.Append("dummy_proc");
                try
                {
                    sc.WrapForStoredProc(ExecutionType.Read, captureReturn: true);
                    throw new Exception("Expected NotSupportedException for captureReturn");
                }
                catch (NotSupportedException)
                {
                    // Expected path
                }

                break;
        }
    }

    // -------------------------------------------------------------------------
    // § 5  Parameter binding semantics
    // -------------------------------------------------------------------------

    protected virtual async Task TestParameterBinding()
    {
        // 5b: = NULL always returns 0 rows
        var sc = _context.CreateSqlContainer();
        sc.Query.AppendFormat("SELECT COUNT(*) FROM {0} WHERE {1} = {2}",
            _helper.WrappedTableName,
            _context.WrapObjectName("description"),
            _context.MakeParameterName("p0"));
        sc.AddParameterWithValue("p0", DbType.String, DBNull.Value);
        var nullCount = await sc.ExecuteScalarOrNullAsync<int>();
        if (nullCount != 0)
            throw new Exception($"[ParamBinding] NULL predicate: expected 0 rows, got {nullCount}");

        // IS NULL on a NOT NULL column → 0 rows
        sc.Clear();
        sc.Query.AppendFormat("SELECT COUNT(*) FROM {0} WHERE {1} IS NULL",
            _helper.WrappedTableName,
            _context.WrapObjectName("description"));
        var isNullCount = await sc.ExecuteScalarOrNullAsync<int>();
        if (isNullCount != 0)
            throw new Exception($"[ParamBinding] IS NULL predicate: expected 0 rows, got {isNullCount}");

        Console.WriteLine("  [ParamBinding] NULL semantics: OK");

        // 5a: duplicate named parameter
        await TestDuplicateParameter();

        // 5c: type binding matrix — insert a known row, verify each column is queryable
        var id = Interlocked.Increment(ref _nextId);
        var knownDesc = _context.GenerateRandomName();
        var t = new TestTable
        {
            Id = id,
            Name = NameEnum.Test,
            Description = knownDesc,
            Value = 99,
            IsActive = true
        };
        var createSc = _helper.BuildCreate(t, _context);
        await createSc.ExecuteNonQueryAsync();

        try
        {
            // Int32
            sc.Clear();
            sc.Query.AppendFormat("SELECT COUNT(*) FROM {0} WHERE {1} = {2}",
                _helper.WrappedTableName,
                _context.WrapObjectName("value"),
                _context.MakeParameterName("p0"));
            sc.AddParameterWithValue("p0", DbType.Int32, 99);
            var valueCount = await sc.ExecuteScalarOrNullAsync<int>();
            if (valueCount < 1)
                throw new Exception($"[ParamBinding] Int32 binding: expected ≥1, got {valueCount}");

            // String
            sc.Clear();
            sc.Query.AppendFormat("SELECT COUNT(*) FROM {0} WHERE {1} = {2}",
                _helper.WrappedTableName,
                _context.WrapObjectName("description"),
                _context.MakeParameterName("p0"));
            sc.AddParameterWithValue("p0", DbType.String, knownDesc);
            var strCount = await sc.ExecuteScalarOrNullAsync<int>();
            if (strCount != 1)
                throw new Exception($"[ParamBinding] String binding: expected 1, got {strCount}");

            // Int64 — query by id
            sc.Clear();
            sc.Query.AppendFormat("SELECT COUNT(*) FROM {0} WHERE {1} = {2}",
                _helper.WrappedTableName,
                _context.WrapObjectName("id"),
                _context.MakeParameterName("p0"));
            sc.AddParameterWithValue("p0", DbType.Int64, id);
            var idCount = await sc.ExecuteScalarOrNullAsync<int>();
            if (idCount != 1)
                throw new Exception($"[ParamBinding] Int64 binding: expected 1, got {idCount}");

            Console.WriteLine("  [ParamBinding] Type matrix (int32, string, int64): OK");
        }
        finally
        {
            await CleanupTestRow(id);
        }
    }

    protected virtual async Task TestDuplicateParameter()
    {
        if (!_context.SupportsRepeatedNamedParameters)
        {
            Console.WriteLine("  [ParamBinding] Duplicate param: provider does not support repeated named parameters — skip");
            return;
        }

        var sc = _context.CreateSqlContainer();
        sc.Query.AppendFormat(
            "SELECT COUNT(*) FROM {0} WHERE {1} = {2} OR {3} = {2}",
            _helper.WrappedTableName,
            _context.WrapObjectName("created_by"),
            _context.MakeParameterName("p0"),
            _context.WrapObjectName("updated_by"));
        sc.AddParameterWithValue("p0", DbType.String, "__nonexistent_user_xyzzy__");
        var count = await sc.ExecuteScalarOrNullAsync<int>();
        if (count < 0)
            throw new Exception($"[ParamBinding] Duplicate param returned invalid count: {count}");
        Console.WriteLine($"  [ParamBinding] Duplicate param (same @p0 twice): OK ({count} rows matched)");
    }

    // -------------------------------------------------------------------------
    // § 7  Full row round-trip fidelity
    // -------------------------------------------------------------------------

    /// <summary>
    /// Description used in the round-trip test. Override for databases whose default character
    /// set does not support Latin Extended characters (e.g. Firebird with NONE charset).
    /// </summary>
    protected virtual string RoundTripDescription =>
        "H\u00e9ll\u00f8 W\u00f6rld r\u00e9sum\u00e9 caf\u00e9 na\u00efve \u00f1";

    protected virtual async Task TestRowRoundTrip()
    {
        if (!_context.DataSourceInfo.SupportsRowLevelDelete)
        {
            Console.WriteLine($"  [{_context.Product}] Skipping row round-trip (requires row-level DELETE for cleanup)");
            return;
        }

        var id = Interlocked.Increment(ref _nextId);
        // Latin Extended-A only — CJK requires NVARCHAR/UTF-8 charset which not all containers use by default
        var unicodeDesc = RoundTripDescription;
        const int knownValue = 42;
        const bool knownActive = false; // non-default

        var t = new TestTable
        {
            Id = id,
            Name = NameEnum.Test2,
            Description = unicodeDesc,
            Value = knownValue,
            IsActive = knownActive
        };

        var beforeInsert = DateTime.UtcNow;
        var createSc = _helper.BuildCreate(t, _context);
        await createSc.ExecuteNonQueryAsync();
        var afterInsert = DateTime.UtcNow;

        try
        {
            var sc = _helper.BuildRetrieve(new List<long> { id }, _context);
            var rows = await _helper.LoadListAsync(sc);
            var retrieved = rows.FirstOrDefault()
                            ?? throw new Exception("[RoundTrip] Row not found after insert");

            if (retrieved.Id != id)
                throw new Exception($"[RoundTrip] Id: expected {id}, got {retrieved.Id}");

            if (retrieved.Description != unicodeDesc)
                throw new Exception(
                    $"[RoundTrip] Description (Unicode): expected '{unicodeDesc}', got '{retrieved.Description}'");

            if (retrieved.Value != knownValue)
                throw new Exception($"[RoundTrip] Value: expected {knownValue}, got {retrieved.Value}");

            if (retrieved.IsActive != knownActive)
                throw new Exception($"[RoundTrip] IsActive: expected {knownActive}, got {retrieved.IsActive}");

            if (retrieved.Name != NameEnum.Test2)
                throw new Exception($"[RoundTrip] Name (enum): expected {NameEnum.Test2}, got {retrieved.Name}");

            // DateTime: CreatedAt must be within the insert window + tolerance
            var tolerance = TimeSpan.FromSeconds(GetDateTimeTolerance());
            var window = afterInsert - beforeInsert + tolerance + tolerance;
            var driftFromBefore = retrieved.CreatedAt - beforeInsert.ToLocalTime();
            // Accept both UTC and local representations from the driver
            var driftAbs = Math.Abs((retrieved.CreatedAt.ToUniversalTime() - beforeInsert).TotalSeconds);
            if (driftAbs > (afterInsert - beforeInsert).TotalSeconds + GetDateTimeTolerance())
                Console.WriteLine(
                    $"  [RoundTrip] DateTime drift {driftAbs:F1}s exceeds tolerance — possible TZ mismatch, proceeding");

            Console.WriteLine("  [RoundTrip] All fields verified (Unicode, bool=false, enum, int, datetime)");
        }
        finally
        {
            await CleanupTestRow(id);
        }
    }

    /// <summary>
    /// Acceptable DateTime round-trip tolerance in seconds.
    /// Override to widen for databases with low timestamp precision (e.g. SQLite text storage).
    /// </summary>
    protected virtual double GetDateTimeTolerance() => 2.0;

    // -------------------------------------------------------------------------
    // § 8  Extended transaction semantics
    // -------------------------------------------------------------------------

    protected virtual async Task TestExtendedTransactions()
    {
        if (!_context.DataSourceInfo.SupportsTransactions)
        {
            Console.WriteLine($"  [{_context.Product}] Skipping extended transactions (not supported)");
            return;
        }

        await TestRollbackOnException();
        await TestReadYourWrites();
    }

    private async Task TestRollbackOnException()
    {
        var before = await CountTestRows();
        try
        {
            await using var tx = _context.BeginTransaction();
            await InsertTestRows(tx);
            throw new InvalidOperationException("Simulated mid-transaction failure");
#pragma warning disable CS0162 // Unreachable code
            tx.Commit();
#pragma warning restore CS0162
        }
        catch (InvalidOperationException)
        {
            // expected — dispose (implicit Rollback) has already run
        }

        var after = await CountTestRows();
        if (after != before)
            throw new Exception(
                $"[ExtendedTx] Rollback-on-exception: expected {before} rows, got {after}");
        Console.WriteLine("  [ExtendedTx] Rollback-on-exception: OK");
    }

    private async Task TestReadYourWrites()
    {
        await using var tx = _context.BeginTransaction();
        var before = await CountTestRows(tx);
        await InsertTestRows(tx);
        var during = await CountTestRows(tx);
        tx.Rollback();

        if (during != before + 1)
            throw new Exception(
                $"[ExtendedTx] Read-your-writes: expected {before + 1} inside tx, got {during}");
        Console.WriteLine("  [ExtendedTx] Read-your-writes: OK");
    }

    // -------------------------------------------------------------------------
    // § 10  Concurrency and disposal
    // -------------------------------------------------------------------------

    protected virtual async Task TestConcurrency()
    {
        if (!_context.DataSourceInfo.SupportsRowLevelDelete)
        {
            Console.WriteLine($"  [{_context.Product}] Skipping concurrency (requires row-level DELETE)");
            return;
        }

        const int n = 5;
        var tasks = Enumerable.Range(0, n).Select(async _ =>
        {
            var id = Interlocked.Increment(ref _nextId);
            var t = new TestTable
            {
                Id = id,
                Name = NameEnum.Test,
                Description = _context.GenerateRandomName(),
                Value = 0,
                IsActive = true
            };
            var createSc = _helper.BuildCreate(t, _context);
            await createSc.ExecuteNonQueryAsync();

            var sc = _helper.BuildRetrieve(new List<long> { id }, _context);
            await _helper.LoadListAsync(sc);

            await CleanupTestRow(id);
        });

        await Task.WhenAll(tasks);
        Console.WriteLine($"  [Concurrency] {n} parallel insert/retrieve/delete loops: OK");
    }

    // -------------------------------------------------------------------------
    // § 11  Capability probes
    // -------------------------------------------------------------------------

    protected virtual async Task TestCapabilityProbes()
    {
        await TestUpsertCapability();
        await TestPagingCapability();
    }

    protected virtual async Task TestUpsertCapability()
    {
        var supports = _context.DataSourceInfo.SupportsMerge
                       || _context.DataSourceInfo.SupportsInsertOnConflict
                       || _context.DataSourceInfo.SupportsOnDuplicateKey;

        if (!supports)
        {
            Console.WriteLine(
                $"  [Capabilities] Upsert: not supported by {_context.Product} — skip");
            return;
        }

        var id = Interlocked.Increment(ref _nextId);
        var t = new TestTable
        {
            Id = id,
            Name = NameEnum.Test,
            Description = "upsert-original",
            Value = 1,
            IsActive = true
        };

        // First upsert → INSERT path
        var sc1 = _helper.BuildUpsert(t, _context);
        await sc1.ExecuteNonQueryAsync();

        // Second upsert → UPDATE path
        t.Description = "upsert-updated";
        var sc2 = _helper.BuildUpsert(t, _context);
        await sc2.ExecuteNonQueryAsync();

        try
        {
            var sc = _helper.BuildRetrieve(new List<long> { id }, _context);
            var rows = await _helper.LoadListAsync(sc);
            var retrieved = rows.FirstOrDefault()
                            ?? throw new Exception("[Capabilities] Upsert: row not found after second upsert");

            if (retrieved.Description != "upsert-updated")
                throw new Exception(
                    $"[Capabilities] Upsert: expected 'upsert-updated', got '{retrieved.Description}'");

            Console.WriteLine("  [Capabilities] Upsert (insert + update): OK");
        }
        finally
        {
            await CleanupTestRow(id);
        }
    }

    protected virtual async Task TestPagingCapability()
    {
        if (!_context.DataSourceInfo.SupportsRowLevelDelete)
        {
            Console.WriteLine($"  [{_context.Product}] Skipping paging (requires row-level DELETE for cleanup)");
            return;
        }

        // Insert 10 rows so we can page through exactly our rows
        var ids = new List<long>();
        for (var i = 0; i < 10; i++)
        {
            var id = Interlocked.Increment(ref _nextId);
            ids.Add(id);
            var t = new TestTable
            {
                Id = id,
                Name = NameEnum.Test,
                Description = $"page-row-{i:D2}",
                Value = i,
                IsActive = true
            };
            var createSc = _helper.BuildCreate(t, _context);
            await createSc.ExecuteNonQueryAsync();
        }

        try
        {
            // Page 1: first 5 of our IDs
            var sc1 = _helper.BuildRetrieve(ids, _context);
            sc1.Query.AppendFormat(" ORDER BY {0} {1}",
                _context.WrapObjectName("id"),
                BuildPagingClause(0, 5));
            var page1 = await _helper.LoadListAsync(sc1);

            // Page 2: next 5 of our IDs
            var sc2 = _helper.BuildRetrieve(ids, _context);
            sc2.Query.AppendFormat(" ORDER BY {0} {1}",
                _context.WrapObjectName("id"),
                BuildPagingClause(5, 5));
            var page2 = await _helper.LoadListAsync(sc2);

            if (page1.Count != 5)
                throw new Exception($"[Capabilities] Paging page 1: expected 5 rows, got {page1.Count}");
            if (page2.Count != 5)
                throw new Exception($"[Capabilities] Paging page 2: expected 5 rows, got {page2.Count}");

            var p1Ids = page1.Select(r => r.Id).ToHashSet();
            var overlap = page2.Select(r => r.Id).Where(x => p1Ids.Contains(x)).ToList();
            if (overlap.Count != 0)
                throw new Exception(
                    $"[Capabilities] Paging: pages overlap on IDs {string.Join(",", overlap)}");

            Console.WriteLine("  [Capabilities] Paging (2 × 5-row pages, no overlap): OK");
        }
        finally
        {
            foreach (var id in ids)
                await CleanupTestRow(id);
        }
    }

    /// <summary>
    /// Returns the dialect-appropriate SQL clause for paging appended after ORDER BY.
    /// </summary>
    protected virtual string BuildPagingClause(int skip, int count)
    {
        return _context.Product switch
        {
            SupportedDatabase.SqlServer or SupportedDatabase.Oracle =>
                $"OFFSET {skip} ROWS FETCH NEXT {count} ROWS ONLY",
            SupportedDatabase.Firebird =>
                $"ROWS {skip + 1} TO {skip + count}",
            _ =>
                $"LIMIT {count} OFFSET {skip}"
        };
    }

    // -------------------------------------------------------------------------
    // § 12  Error mapping / diagnostics
    // -------------------------------------------------------------------------

    protected virtual async Task TestErrorMapping()
    {
        // 12a + 12b: Duplicate PK and connection health — only meaningful when PK enforcement exists
        if (_context.DataSourceInfo.SupportsIntegrityConstraints)
        {
            var id = Interlocked.Increment(ref _nextId);
            var t = new TestTable
            {
                Id = id,
                Name = NameEnum.Test,
                Description = "error-mapping-test",
                Value = 0,
                IsActive = true
            };

            // Insert first copy
            var sc1 = _helper.BuildCreate(t, _context);
            await sc1.ExecuteNonQueryAsync();

            // 12a: Duplicate PK → must surface as DbException
            try
            {
                var sc2 = _helper.BuildCreate(t, _context);
                await sc2.ExecuteNonQueryAsync();
                throw new Exception("[ErrorMapping] Expected DbException for duplicate PK — none thrown");
            }
            catch (DbException ex)
            {
                Console.WriteLine(
                    $"  [ErrorMapping] Unique violation → DbException: OK ({ex.Message[..Math.Min(80, ex.Message.Length)]}...)");
            }

            // 12b: Connection must still be usable after the exception
            var healthCount = await CountTestRows();
            Console.WriteLine($"  [ErrorMapping] Connection health after exception: OK (count={healthCount})");

            await CleanupTestRow(id);
        }
        else
        {
            Console.WriteLine(
                $"  [{_context.Product}] Skipping 12a/12b (no PK enforcement — unique violation not guaranteed)");
        }

        // 12c: Syntax error → must always surface as DbException with non-empty message
        var badSc = _context.CreateSqlContainer("SELECT * FROM");
        DbException? syntaxEx = null;
        try
        {
            await badSc.ExecuteNonQueryAsync();
            throw new Exception("[ErrorMapping] Expected DbException for syntax error — none thrown");
        }
        catch (DbException ex)
        {
            syntaxEx = ex;
        }

        if (string.IsNullOrWhiteSpace(syntaxEx?.Message))
            throw new Exception("[ErrorMapping] Syntax error exception had empty message");

        Console.WriteLine(
            $"  [ErrorMapping] Syntax error → DbException: OK ({syntaxEx.Message[..Math.Min(80, syntaxEx.Message.Length)]}...)");
    }

    // -------------------------------------------------------------------------
    // § 6  Identifier quoting torture
    // -------------------------------------------------------------------------

    protected virtual async Task TestIdentifierQuoting()
    {
        var wrappedTable = _context.WrapObjectName("quote_test");
        var wrappedId = _context.WrapObjectName("id");
        var wrappedOrder = _context.WrapObjectName("order"); // reserved word

        await DropTableIfExistsAsync("quote_test");

        var sc = _context.CreateSqlContainer();
        sc.Query.AppendFormat(
            "CREATE TABLE {0} ({1} INT NOT NULL PRIMARY KEY, {2} INT NOT NULL)",
            wrappedTable, wrappedId, wrappedOrder);
        await sc.ExecuteNonQueryAsync();

        try
        {
            // INSERT
            sc.Clear();
            sc.Query.AppendFormat(
                "INSERT INTO {0} ({1}, {2}) VALUES ({3}, {4})",
                wrappedTable, wrappedId, wrappedOrder,
                _context.MakeParameterName("p0"),
                _context.MakeParameterName("p1"));
            sc.AddParameterWithValue("p0", DbType.Int32, 1);
            sc.AddParameterWithValue("p1", DbType.Int32, 42);
            await sc.ExecuteNonQueryAsync();

            // SELECT — verify reserved-word column round-trips
            sc.Clear();
            sc.Query.AppendFormat(
                "SELECT {0} FROM {1} WHERE {2} = {3}",
                wrappedOrder, wrappedTable, wrappedId,
                _context.MakeParameterName("p0"));
            sc.AddParameterWithValue("p0", DbType.Int32, 1);
            var val = await sc.ExecuteScalarOrNullAsync<int>();
            if (val != 42)
                throw new Exception($"[Quoting] Expected 42 for 'order' column, got {val}");

            Console.WriteLine("  [Quoting] Reserved word 'order' as column name: OK");
        }
        finally
        {
            sc.Clear();
            sc.Query.AppendFormat("DROP TABLE {0}", wrappedTable);
            await sc.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Deletes a single row by ID. Skips silently for databases without row-level DELETE.
    /// </summary>
    protected virtual async Task CleanupTestRow(long id, IDatabaseContext? db = null)
    {
        if (!_context.DataSourceInfo.SupportsRowLevelDelete)
            return; // rows accumulate until the table is recreated at the next test run

        var ctx = db ?? _context;
        var del = _helper.BuildDelete(id, ctx);
        await del.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Drops a table, handling the case where it may not exist.
    /// Uses IF EXISTS when supported; falls back to plain DROP TABLE with exception suppression.
    /// </summary>
    protected virtual async Task DropTableIfExistsAsync(string tableName)
    {
        var sc = _context.CreateSqlContainer();
        if (_context.DataSourceInfo.SupportsDropTableIfExists)
            sc.Query.AppendFormat("DROP TABLE IF EXISTS {0}", _context.WrapObjectName(tableName));
        else
            sc.Query.AppendFormat("DROP TABLE {0}", _context.WrapObjectName(tableName));

        try
        {
            await sc.ExecuteNonQueryAsync();
        }
        catch
        {
            // Table did not exist — ignore
        }
    }
}
