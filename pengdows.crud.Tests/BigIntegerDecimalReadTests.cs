using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A NUMERIC wider than decimal (PostgreSQL's NUMERIC(1000,0), CockroachDB and YugabyteDB through the same
/// driver) is reported as decimal, and the driver's typed read throws OverflowException beyond decimal's range.
/// A BigInteger property must still read the value, from the number's text, as WideIntegerFieldReader already
/// does for an Int64-reported column. Without the fallback, the whole row fails to hydrate.
/// </summary>
public sealed class BigIntegerDecimalReadTests
{
    private const string Wide = "123456789012345678901234567890";   // 30 digits

    [Fact]
    public async Task RetrieveOneAsync_BigIntegerBeyondDecimal_ReadsTheExactValue()
    {
        var (context, exec) = Context(SupportedDatabase.PostgreSql);
        await using var _ = context;
        exec.EnqueueReaderResult(WideReader(Wide));
        var gateway = new TableGateway<WideRow, int>(context);

        var row = await gateway.RetrieveOneAsync(1);

        Assert.Equal(BigInteger.Parse(Wide, CultureInfo.InvariantCulture), row!.N);
    }

    [Fact]
    public async Task RetrieveOneAsync_BigIntegerWithinDecimal_ReadsTheExactValue()
    {
        var (context, exec) = Context(SupportedDatabase.PostgreSql);
        await using var _ = context;
        exec.EnqueueReaderResult(WideReader("12"));
        var gateway = new TableGateway<WideRow, int>(context);

        var row = await gateway.RetrieveOneAsync(1);

        Assert.Equal(new BigInteger(12), row!.N);
    }

    private static fakeDbDataReader WideReader(string digits)
    {
        var rows = new List<Dictionary<string, object>> { new() { ["id"] = 1, ["n"] = digits } };
        return new fakeDbDataReader(rows) { DecimalTextColumns = new HashSet<string> { "n" } };
    }

    private static (DatabaseContext Context, fakeDbConnection Exec) Context(SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = database });
        var exec = new fakeDbConnection { EmulatedProduct = database };
        factory.Connections.Add(exec);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Server=x;Database=y;EmulatedProduct={database}",
            DbMode = DbMode.Standard
        }, factory);
        return (context, exec);
    }

    [Table("wide")]
    public sealed class WideRow
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("n", DbType.Object)]
        public BigInteger N { get; set; }
    }
}
