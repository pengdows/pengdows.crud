using System.Data;
using System.Data.Common;
using pengdows.crud.enums;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// Diagnostic matrix for the optional ADO.NET DataTypes schema collection. This deliberately
/// reports unsupported or incomplete provider metadata instead of treating it as an integration
/// failure: the purpose is to discover the provider contract before hydration depends on it.
/// </summary>
[Collection("IntegrationTests")]
public sealed class DataTypesSchemaProbeTests : DatabaseTestBase
{
    public DataTypesSchemaProbeTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context) =>
        Task.CompletedTask;

    protected override Task CleanupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context) =>
        Task.CompletedTask;

    [SkippableFact]
    public async Task DataTypesSchemaCollection_ProbeEveryEnabledProvider()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var databaseContext = Assert.IsType<DatabaseContext>(context);
            await using var connection = databaseContext.Factory.CreateConnection();
            Assert.NotNull(connection);
            connection!.ConnectionString = databaseContext.RawConnectionString;
            await connection.OpenAsync();

            DataTable schema;
            try
            {
                schema = connection.GetSchema(DbMetaDataCollectionNames.DataTypes);
            }
            catch (Exception ex)
            {
                Output.WriteLine($"{provider}: UNSUPPORTED/FAILED; {ex.GetType().Name}: {ex.Message}");
                return;
            }

            var columns = schema.Columns.Cast<DataColumn>()
                .Select(column => column.ColumnName)
                .ToArray();
            var typeNameColumn = FindColumn(schema, "TypeName");
            var clrTypeColumn = FindColumn(schema, "DataType");
            var providerTypeColumn = FindColumn(schema, "ProviderDbType");

            var useful = typeNameColumn != null && clrTypeColumn != null;
            var examples = useful
                ? schema.Rows.Cast<DataRow>()
                    .Take(5)
                    .Select(row => $"{row[typeNameColumn!]}=>{row[clrTypeColumn!]}".Replace(" ", string.Empty))
                    .ToArray()
                : Array.Empty<string>();

            Output.WriteLine(
                $"{provider}: SUPPORTED; rows={schema.Rows.Count}; useful={useful}; " +
                $"TypeName={typeNameColumn != null}; DataType={clrTypeColumn != null}; " +
                $"ProviderDbType={providerTypeColumn != null}; columns=[{string.Join(",", columns)}]; " +
                $"examples=[{string.Join(",", examples)}]");
        });
    }

    private static string? FindColumn(DataTable schema, string name) =>
        schema.Columns.Cast<DataColumn>()
            .FirstOrDefault(column => string.Equals(column.ColumnName, name, StringComparison.OrdinalIgnoreCase))
            ?.ColumnName;
}
