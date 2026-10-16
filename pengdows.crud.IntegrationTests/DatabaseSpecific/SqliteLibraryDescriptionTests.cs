using System.Data;
using Microsoft.Data.Sqlite;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.dialects;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// The SQLite dialect describes the library it is actually connected to: DataSourceInformation from the provider
/// (or, when the provider has none, from the live database), and the parameter limit and JSON/window-function
/// support from the library's own catalogs. fakeDb can only replay what it is told, so these run on real SQLite.
/// </summary>
[Collection("IntegrationTests")]
public class SqliteLibraryDescriptionTests
{
    private readonly ITestOutputHelper _output;

    public SqliteLibraryDescriptionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static DatabaseContext NewContext() => new(new DatabaseContextConfiguration
    {
        ConnectionString = "Data Source=:memory:",
        DbMode = DbMode.SingleConnection,
        ReadWriteMode = ReadWriteMode.ReadWrite
    }, SqliteFactory.Instance);

    private static async Task<T?> Scalar<T>(IDatabaseContext ctx, string sql)
    {
        await using var sc = ctx.CreateSqlContainer(sql);
        return await sc.ExecuteScalarOrNullAsync<T>();
    }

    [SkippableFact]
    public async Task DataSourceInformation_ReportsTheVersionOfTheLoadedLibrary()
    {
        Skip.IfNot(IntegrationTestConfiguration.IsEnabled(SupportedDatabase.Sqlite), "Sqlite is not enabled for this test run.");
        await using var ctx = NewContext();
        var live = await Scalar<string>(ctx, "SELECT sqlite_version()");
        await using var connection = ctx.GetConnection(ExecutionType.Read);

        var table = ctx.Dialect.GetDataSourceInformationSchema(connection);

        _output.WriteLine($"sqlite_version()={live}; schema={table.Rows[0]["DataSourceProductVersion"]}");
        Assert.Equal("SQLite", table.Rows[0]["DataSourceProductName"]);
        Assert.Equal(live, table.Rows[0]["DataSourceProductVersion"]);
    }

    [SkippableFact]
    public async Task DialectCapabilities_AgreeWithTheLibrarysOwnCatalogs()
    {
        Skip.IfNot(IntegrationTestConfiguration.IsEnabled(SupportedDatabase.Sqlite), "Sqlite is not enabled for this test run.");
        await using var ctx = NewContext();

        var hasJsonb = await Scalar<long>(ctx, "SELECT count(*) FROM pragma_function_list WHERE name = 'jsonb'") > 0;
        var hasWindow = await Scalar<long>(ctx, "SELECT count(*) FROM pragma_function_list WHERE name = 'row_number'") > 0;
        var maxVariables = await Scalar<string>(ctx,
            "SELECT compile_options FROM pragma_compile_options WHERE compile_options LIKE 'MAX_VARIABLE_NUMBER=%'");
        _output.WriteLine($"jsonb={hasJsonb} window={hasWindow} max={maxVariables} limit={ctx.Dialect.MaxParameterLimit}");

        Assert.Equal(hasJsonb, ctx.Dialect.SupportsJsonTypes);
        Assert.Equal(hasWindow, ctx.Dialect.SupportsWindowFunctions);
        if (maxVariables != null)
        {
            Assert.Equal(int.Parse(maxVariables["MAX_VARIABLE_NUMBER=".Length..]), ctx.Dialect.MaxParameterLimit);
        }
        else
        {
            // Not overridden at compile time: the library default for its version.
            var parts = (await Scalar<string>(ctx, "SELECT sqlite_version()"))!.Split('.');
            var atLeast332 = int.Parse(parts[0]) > 3 || (int.Parse(parts[0]) == 3 && int.Parse(parts[1]) >= 32);
            Assert.Equal(atLeast332 ? 32766 : 999, ctx.Dialect.MaxParameterLimit);
        }
    }
}
