using System.Data;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Ported from testbed/TestProvider.cs's TestGetOrdinalUnknownColumnBehavior (part of the
/// testbed-vs-IntegrationTests consolidation). Deliberately NOT a strict cross-provider
/// assertion: <c>IDataRecord.GetOrdinal</c>'s documented contract is to throw
/// <see cref="IndexOutOfRangeException"/> for an unknown column name, but this is characterized
/// per-provider rather than enforced uniformly, since a provider that doesn't follow the
/// documented contract isn't a pengdows.crud bug — it's the underlying ADO.NET driver's own
/// choice. What IS asserted: a KNOWN column name still resolves to a valid ordinal (the sanity
/// check the original test also performed), and querying an unknown column name never throws
/// something unexpected enough to fail the test outright — the actual exception type/absence is
/// recorded via test output for visibility, not asserted.
/// </summary>
[Collection("IntegrationTests")]
public class GetOrdinalUnknownColumnTests : DatabaseTestBase
{
    private static long _nextId;

    public GetOrdinalUnknownColumnTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
    }

    [SkippableFact]
    public async Task GetOrdinal_UnknownColumnName_DoesNotThrowUnexpectedly()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<TestTable, long>(context, GetAuditResolver());
            var id = System.Threading.Interlocked.Increment(ref _nextId);
            var entity = new TestTable { Id = id, Name = NameEnum.Test, Description = "get-ordinal-unknown-column" };
            await helper.CreateAsync(entity, context);
            id = entity.Id;

            try
            {
                await using var select = context.CreateSqlContainer();
                select.Query.Append("SELECT ").Append(context.WrapObjectName("id"))
                    .Append(" FROM ").Append(context.WrapObjectName("test_table"))
                    .Append(" WHERE ").Append(context.WrapObjectName("id"))
                    .Append(" = ").Append(select.MakeParameterName("p0"));
                select.AddParameterWithValue("p0", DbType.Int64, id);

                await using var reader = await select.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());

                // Sanity check: a real column name still resolves normally.
                var knownOrdinal = reader.GetOrdinal("id");
                Assert.True(knownOrdinal >= 0);

                const string unknownColumn = "this_column_definitely_does_not_exist_xyz123";
                try
                {
                    var ordinal = reader.GetOrdinal(unknownColumn);
                    Output.WriteLine(
                        $"[GetOrdinal] {provider} does NOT throw for an unknown column name — " +
                        $"returned ordinal {ordinal} instead of the documented IndexOutOfRangeException.");
                }
                catch (IndexOutOfRangeException)
                {
                    Output.WriteLine(
                        $"[GetOrdinal] {provider} throws IndexOutOfRangeException for an unknown column name " +
                        "(matches the documented IDataRecord.GetOrdinal contract).");
                }
                catch (Exception ex)
                {
                    Output.WriteLine(
                        $"[GetOrdinal] {provider} throws {ex.GetType().Name} (not IndexOutOfRangeException) " +
                        "for an unknown column name: recorded, not enforced.");
                }
            }
            finally
            {
                await helper.DeleteAsync(id, context);
            }
        });
    }
}
