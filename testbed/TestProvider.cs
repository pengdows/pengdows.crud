#region

using Microsoft.Extensions.DependencyInjection;
using pengdows.crud;
using pengdows.crud.enums;

#endregion

namespace testbed;

public class TestProvider : IAsyncTestProvider
{
    private static long _nextId;

    private readonly IDatabaseContext _context;
    private readonly EntityHelper<TestTable, long> _helper;

    public TestProvider(IDatabaseContext databaseContext, IServiceProvider serviceProvider)
    {
        _context = databaseContext;
        var resolver = serviceProvider.GetService<IAuditValueResolver>() ??
                       new TestAuditValueResolver("system");
        _helper = new EntityHelper<TestTable, long>(databaseContext, resolver);
    }


    public async Task RunTest()
    {
        Console.WriteLine("Completed testing of provider:" + _context.Product);
        try
        {
            Console.WriteLine("Running Create table");
            await CreateTable();

            Console.WriteLine("Running Insert rows");
            var before = await CountTestRows();
            var id = await InsertTestRows();
            var afterInsert = await CountTestRows();
            if (afterInsert != before + 1)
            {
                throw new Exception("Insert did not affect expected row count");
            }

            Console.WriteLine("Running retrieve rows");
            var obj = await RetrieveRows(id);
            if (obj.Id != id)
            {
                throw new Exception("Retrieved row did not match inserted id");
            }

            Console.WriteLine("Running delete rows");
            await DeletedRow(obj);
            var afterDelete = await CountTestRows();
            if (afterDelete != before)
            {
                throw new Exception("Delete did not affect expected row count");
            }

            Console.WriteLine("Running Transaction rows");
            await TestTransactions();
            Console.WriteLine("Running stored procedure return value test");
            await TestStoredProcReturnValue();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to complete tests successfully: " + ex.Message);
        }
        finally
        {
            Console.WriteLine("Completed testing of provider:" + _context.Product);
        }
    }

    private async Task TestTransactions()
    {
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
        var count = await sc.ExecuteScalarAsync<int>();
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

    private async Task<long> InsertTestRows(IDatabaseContext? db = null)
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

    private async Task DeletedRow(TestTable t, IDatabaseContext? db = null)
    {
        var ctx = db ?? _context;
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
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.DuckDB => "BOOLEAN",
            SupportedDatabase.Firebird => "SMALLINT",
            SupportedDatabase.Oracle => "NUMBER(1)",
            SupportedDatabase.MySql => "BOOLEAN",
            SupportedDatabase.MariaDb => "BOOLEAN",
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
                var value = await sc.ExecuteScalarAsync<int>();
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
}