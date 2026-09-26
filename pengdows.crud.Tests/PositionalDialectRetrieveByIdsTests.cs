using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CONFIRMED live (Informix, HARN-005): TableGateway.RetrieveAsync with one or two ids threw
/// KeyNotFoundException "Parameter 'p0' not found" on a positional (unnamed-parameter) dialect. The
/// cached retrieve templates name their id parameters w0/w1, and the gateway addressed them as p0/p1,
/// which SqlContainer.SetParameterValue maps to w0/w1 only on named-parameter dialects.
/// </summary>
public class PositionalDialectRetrieveByIdsTests
{
    [Table("positional_rows")]
    public class PositionalRow
    {
        [Id(true)][Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
    }

    [Theory]
    [InlineData(SupportedDatabase.Informix, 1)]
    [InlineData(SupportedDatabase.Informix, 2)]
    [InlineData(SupportedDatabase.Informix, 3)]
    [InlineData(SupportedDatabase.SqlServer, 2)]
    public async Task RetrieveAsync_SmallIdLists_ExecuteOnEveryParameterStyle(SupportedDatabase db, int count)
    {
        await using var context = new DatabaseContext($"Data Source=test;EmulatedProduct={db}", new fakeDbFactory(db));
        var gateway = new TableGateway<PositionalRow, long>(context);
        var ids = new long[count];
        for (var i = 0; i < count; i++)
        {
            ids[i] = i + 1;
        }

        var rows = await gateway.RetrieveAsync(ids, context);

        Assert.NotNull(rows);
    }

    [Theory]
    [InlineData(SupportedDatabase.Informix)]
    [InlineData(SupportedDatabase.SqlServer)]
    public async Task RetrieveStreamAsync_TwoIds_ExecutesOnEveryParameterStyle(SupportedDatabase db)
    {
        await using var context = new DatabaseContext($"Data Source=test;EmulatedProduct={db}", new fakeDbFactory(db));
        var gateway = new TableGateway<PositionalRow, long>(context);

        var count = 0;
        await foreach (var _ in gateway.RetrieveStreamAsync(new long[] { 1, 2 }, context))
        {
            count++;
        }

        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(SupportedDatabase.Informix)]
    [InlineData(SupportedDatabase.SqlServer)]
    public async Task RetrieveOneAsync_ById_ExecutesOnEveryParameterStyle(SupportedDatabase db)
    {
        await using var context = new DatabaseContext($"Data Source=test;EmulatedProduct={db}", new fakeDbFactory(db));
        var gateway = new TableGateway<PositionalRow, long>(context);

        var row = await gateway.RetrieveOneAsync(1, context);

        Assert.Null(row);
    }
}
