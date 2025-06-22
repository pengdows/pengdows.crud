#region

using pengdows.crud;

#endregion

namespace testbed;

public class TestProvider : IAsyncTestProvider
{
    private readonly IDatabaseContext _context;
    private readonly EntityHelper<TestTable, long> _helper;

    public TestProvider(IDatabaseContext databaseContext, IServiceProvider serviceProvider)
    {
        _context = databaseContext; //serviceProvider.GetService<IAuditValueResolver>()
        _helper = new EntityHelper<TestTable, long>(databaseContext);
    }


    public async Task RunTest()
    {
        Console.WriteLine("Completed testing of provider:" + _context.Product.ToString());
        try
        {
            Console.WriteLine("Running Create table");
            await CreateTable();

            Console.WriteLine("Running Insert rows");
            var id = await InsertTestRows();
            Console.WriteLine("Running test count");
            await CountTestRows();
            Console.WriteLine("Running retrieve rows");
            var obj = await RetrieveRows(id);
            Console.WriteLine("Running delete rows");
            await DeletedRow(obj);
            Console.WriteLine("Running Transaction rows");
            await TestTransactions();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to complete tests successfully: " + ex.Message);
        }
        finally
        {
            Console.WriteLine("Completed testing of provider:" + _context.Product.ToString());
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
        var transaction = _context.BeginTransaction();
        var id = await InsertTestRows(transaction);
        var count = await CountTestRows(transaction);
        transaction.Commit();
    }

    private async Task TestRollbackTransaction()
    {
        var transaction = _context.BeginTransaction();
        var id = await InsertTestRows(transaction);
        var count = await CountTestRows(transaction);
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

        sqlContainer.Query.Clear();
        sqlContainer.Query.AppendFormat(@"
CREATE TABLE {0}test_table{1} (
    {0}id{1} BIGINT  NOT NULL UNIQUE, 
    {0}name{1} VARCHAR(100) NOT NULL,
    {0}description{1} VARCHAR(1000) NOT NULL,
    {0}created_at{1} DATETIME NOT NULL,
    {0}created_by{1} VARCHAR(100) NOT NULL,
    {0}updated_at{1} DATETIME NOT NULL,
    {0}updated_by{1} VARCHAR(100) NOT NULL
    
); ", qp, qs);
        try
        {
            await sqlContainer.ExecuteNonQueryAsync();
        }
        catch (Exception e)
        {
            try
            {
                sqlContainer.Query.Clear();
                sqlContainer.Query.AppendFormat("TRUNCATE TABLE {0}test_table{1}", qp, qs);
                await sqlContainer.ExecuteNonQueryAsync();
            }
            catch
            {
                //eat error quitely if it doesn't support truncate table
            }

            Console.WriteLine(e.Message + "\n --- Continuing anyways");
        }
    }

    private async Task<long> InsertTestRows(IDatabaseContext? db = null)
    {
        var ctx = db ?? _context;
        var name = ctx is TransactionContext ? NameEnum.Test2 : NameEnum.Test;
        var t = new TestTable
        {
            Id = Random.Shared.Next(),
            Name = name,
            Description = ctx.GenerateRandomName()
        };
        var sq = _helper.BuildCreate(t, ctx);
        await sq.ExecuteNonQueryAsync();
        return t.Id;
    }

    private async Task<TestTable> RetrieveRows(long id, IDatabaseContext? db = null)
    {
        var arr = new List<long>() { id };
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
        if (count != 1) throw new Exception("Delete failed");
    }
}