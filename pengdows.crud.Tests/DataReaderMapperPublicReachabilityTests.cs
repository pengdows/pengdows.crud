using System.Data;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// FEAT-004: DataReaderMapper/IDataReaderMapper/IMapperOptions were declared as a general-purpose
// "map any object you like, from any SQL result you like" feature — the documented use case
// (confirmed with the maintainer) is a stored procedure (or any query) whose result shape has no
// corresponding entity/table pengdows.poco.mint could ever generate a POCO for, but the caller
// still wants to hydrate an arbitrary POCO by column-name matching. IDataReaderMapper/IMapperOptions
// were already public interfaces in pengdows.crud.abstractions, and MapperOptions (the concrete
// options record) was already public and constructible — but DataReaderMapper, the only
// implementation, and its Instance singleton, were `internal sealed`, so no external consumer
// could ever actually obtain an IDataReaderMapper to use. The public interfaces were real but
// structurally unreachable. Made DataReaderMapper public to close that gap.
public class DataReaderMapperPublicReachabilityTests
{
    // A plain POCO with zero [Table]/[Column]/[Id] attributes — deliberately not something
    // pengdows.poco.mint could generate, since it corresponds to no real table. Only public
    // settable properties, matched to reader columns by name (case-insensitive).
    private sealed class AdHocProcResult
    {
        public int OrderId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public decimal TotalDue { get; set; }
    }

    [Fact]
    public void DataReaderMapper_IsPublic()
    {
        Assert.True(typeof(DataReaderMapper).IsPublic,
            "DataReaderMapper is the only implementation of the public IDataReaderMapper " +
            "contract — it must be public itself, or the interface is unreachable by any " +
            "external consumer despite being part of the public API surface.");
    }

    [Fact]
    public void DataReaderMapper_Instance_IsPubliclyReachableAsIDataReaderMapper()
    {
        IDataReaderMapper mapper = DataReaderMapper.Instance;
        Assert.NotNull(mapper);
    }

    [Fact]
    public async Task LoadAsync_HydratesArbitraryPocoWithNoEntityAttributes_FromAnArbitraryResultShape()
    {
        // Simulates a stored-procedure-shaped result set: column aliases baked into the query
        // (the way a real stored proc's own SELECT commonly shapes its output for a consumer),
        // with no corresponding registered entity. Uses a plain SELECT rather than a literal
        // stored procedure invocation, since fakeDb's in-memory persistence engine has no
        // stored-procedure execution support to simulate against — DataReaderMapper only cares
        // about the reader's column shape, not how the command that produced it was invoked, so
        // this exercises the same mapping path a real stored-procedure result would.
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite) { EnableDataPersistence = true };
        await using var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);

        using (var setup = context.CreateSqlContainer(
                   "CREATE TABLE ad_hoc_proc_shape (order_id INTEGER, customer_name TEXT, total_due REAL)"))
        {
            await setup.ExecuteNonQueryAsync();
        }

        using (var insert = context.CreateSqlContainer(
                   "INSERT INTO ad_hoc_proc_shape (order_id, customer_name, total_due) VALUES (42, 'Acme Corp', 199.95)"))
        {
            await insert.ExecuteNonQueryAsync();
        }

        using var select = context.CreateSqlContainer(
            "SELECT order_id AS OrderId, customer_name AS CustomerName, total_due AS TotalDue FROM ad_hoc_proc_shape");
        await using var reader = await select.ExecuteReaderAsync(CommandType.Text);

        IDataReaderMapper mapper = DataReaderMapper.Instance;
        var results = await mapper.LoadAsync<AdHocProcResult>(reader);

        var result = Assert.Single(results);
        Assert.Equal(42, result.OrderId);
        Assert.Equal("Acme Corp", result.CustomerName);
        Assert.Equal(199.95m, result.TotalDue);
    }
}
