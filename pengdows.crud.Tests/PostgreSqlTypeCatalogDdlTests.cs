using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CONFIRMED live (PostgreSQL, full integration run): Npgsql caches the type catalog per data
/// source. After CREATE EXTENSION hstore ran through a context, reads through its reader data
/// source (whose catalog was loaded before the extension existed) failed with "Reading as
/// 'System.Object' is not supported for fields having DataTypeName '-'". DDL that changes the type
/// catalog must reload types on every data source the context owns.
/// </summary>
public class PostgreSqlTypeCatalogDdlTests
{
    private static (DatabaseContext Context, fakeDbFactory Factory) Create(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db) { SupportsNativeDataSource = true };
        var context = new DatabaseContext($"Host=localhost;Database=db;EmulatedProduct={db}", factory);
        return (context, factory);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql, "CREATE EXTENSION IF NOT EXISTS hstore")]
    [InlineData(SupportedDatabase.PostgreSql, "create type mood as enum ('happy', 'sad')")]
    [InlineData(SupportedDatabase.PostgreSql, "ALTER TYPE mood ADD VALUE 'meh'")]
    [InlineData(SupportedDatabase.PostgreSql, "DROP EXTENSION hstore")]
    [InlineData(SupportedDatabase.PostgreSql, "CREATE DOMAIN positive AS integer CHECK (VALUE > 0)")]
    [InlineData(SupportedDatabase.YugabyteDb, "CREATE EXTENSION IF NOT EXISTS hstore")]
    public async Task TypeCatalogDdl_ReloadsTypesOnEveryOwnedDataSource(SupportedDatabase db, string sql)
    {
        var (context, factory) = Create(db);
        using (context)
        {
            await using (var ddl = context.CreateSqlContainer(sql))
            {
                await ddl.ExecuteNonQueryAsync();
            }

            Assert.True(factory.CreatedDataSources.Count >= 2, "expected separate writer and reader data sources");
            var live = factory.CreatedDataSources.Where(d => d.ReloadTypesCount > 0).ToList();
            Assert.Contains(live, d => d.ConnectionString == context.RawConnectionString);
            Assert.Contains(live, d => d.ConnectionString == context.RawReaderConnectionString);
        }
    }

    [Theory]
    [InlineData("CREATE TABLE t (id integer)")]
    [InlineData("INSERT INTO t VALUES (1)")]
    public async Task OtherStatements_DoNotReloadTypes(string sql)
    {
        var (context, factory) = Create(SupportedDatabase.PostgreSql);
        using (context)
        {
            await using (var statement = context.CreateSqlContainer(sql))
            {
                await statement.ExecuteNonQueryAsync();
            }

            Assert.All(factory.CreatedDataSources, d => Assert.Equal(0, d.ReloadTypesCount));
        }
    }
}
