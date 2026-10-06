using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// HARN-016, confirmed live (Oracle Free 23, ODP.NET 23): every OracleDataSource owns a private
/// connection pool, even for an identical connection string, and disposing the data source leaves
/// that pool's idle connections open until the process exits. A context creates one for writes and
/// one for reads, so each context disposed left up to two server sessions behind (10 contexts: +10
/// sessions reading only; 30 contexts writing and reading: +60), which ran Oracle Free (200 processes)
/// out under the type matrix. OracleDataSource.ClearPool() before disposal closes them (live: 10
/// contexts, +0) and touches no other data source's pool. A context clears only the data sources it
/// created; one passed in belongs to the caller.
/// </summary>
public sealed class DataSourcePoolClearOnDisposeTests
{
    private static (DatabaseContext Context, fakeDbFactory Factory) Oracle()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle) { SupportsNativeDataSource = true };
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=x;EmulatedProduct=Oracle",
            DbMode = DbMode.Standard
        }, factory);
        return (context, factory);
    }

    [Fact]
    public void Dispose_Oracle_ClearsThePoolOfEveryDataSourceItCreated()
    {
        var (context, factory) = Oracle();
        Assert.NotEmpty(factory.CreatedDataSources);

        context.Dispose();

        Assert.All(factory.CreatedDataSources, ds => Assert.Equal(1, ds.ClearPoolCount));
    }

    [Fact]
    public async Task DisposeAsync_Oracle_ClearsThePoolOfEveryDataSourceItCreated()
    {
        var (context, factory) = Oracle();
        Assert.NotEmpty(factory.CreatedDataSources);

        await context.DisposeAsync();

        Assert.All(factory.CreatedDataSources, ds => Assert.Equal(1, ds.ClearPoolCount));
    }

    [Fact]
    public async Task DisposeAsync_OtherDatabase_LeavesPoolsToTheProvider()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql) { SupportsNativeDataSource = true };
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Host=x;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard
        }, factory);
        Assert.NotEmpty(factory.CreatedDataSources);

        await context.DisposeAsync();

        Assert.All(factory.CreatedDataSources, ds => Assert.Equal(0, ds.ClearPoolCount));
    }

    [Fact]
    public async Task DisposeAsync_Oracle_NeverClearsADataSourceItWasGiven()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        var given = new FakeDbDataSource("Data Source=x;EmulatedProduct=Oracle", factory);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=x;EmulatedProduct=Oracle",
            DbMode = DbMode.Standard
        }, given, factory);

        await context.DisposeAsync();

        Assert.Equal(0, given.ClearPoolCount);
    }
}
