using System.Numerics;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

[Table("wide_ints")]
public class WideIntegerSelectEntity
{
    [Id]
    [Column("id", System.Data.DbType.Int32)]
    public int Id { get; set; }

    [Column("n", System.Data.DbType.Object)]
    public BigInteger N { get; set; }
}

/// <summary>
/// A BigInteger column is selected as its exact text on a database whose driver cannot return the digits of a numeric
/// wider than a decimal (Npgsql, MySqlConnector, Microsoft.Data.SqlClient, ODP.NET). The gateway asks the dialect for the
/// cast; a database whose driver already returns the text needs none.
/// </summary>
public class WideIntegerSelectTests
{
    [Theory]
    [InlineData(SupportedDatabase.PostgreSql, "AS TEXT")]
    [InlineData(SupportedDatabase.MySql, "AS CHAR")]
    [InlineData(SupportedDatabase.SqlServer, "AS NVARCHAR")]
    [InlineData(SupportedDatabase.Oracle, "TO_CHAR(")]
    [InlineData(SupportedDatabase.Firebird, "AS VARCHAR")]
    [InlineData(SupportedDatabase.FlatFile, "AS VARCHAR")]
    public void WideIntegerIsSelectedAsText(SupportedDatabase product, string cast)
    {
        var sql = BuildSelect(product);

        Assert.Contains(cast, sql);
    }

    [Fact]
    public void SqliteNeedsNoCast()
    {
        var sql = BuildSelect(SupportedDatabase.Sqlite);

        Assert.DoesNotContain("CAST(", sql);
    }

    private static string BuildSelect(SupportedDatabase product)
    {
        using var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new fakeDbFactory(product));
        var gateway = new TableGateway<WideIntegerSelectEntity, int>(context);
        using var sc = gateway.BuildBaseRetrieve("t");
        return sc.Query.ToString();
    }
}

/// <summary>
/// Sybase ASE refuses an implicit text-to-NUMERIC conversion, so a wide integer written as its text is converted in the
/// statement (CONVERT to NUMERIC(38,0), the width the column is declared with).
/// </summary>
public class WideIntegerInsertTests
{
    [Fact]
    public void SybaseConvertsTheWideIntegerTextInTheInsert()
    {
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=SybaseASE", new fakeDbFactory(SupportedDatabase.SybaseASE));
        var gateway = new TableGateway<WideIntegerSelectEntity, int>(context);

        using var sc = gateway.BuildCreate(new WideIntegerSelectEntity { Id = 1, N = 7 });

        Assert.Contains("CONVERT(NUMERIC(38,0),", sc.Query.ToString());
    }

    [Fact]
    public void FirebirdConvertsTheWideIntegerTextInTheInsert()
    {
        // The driver converts a parameter to the decimal type of the NUMERIC column it is inserted into, and a decimal
        // holds 28 digits: a 30-digit value overflows there. The text stays a VARCHAR parameter, and the statement casts
        // it to NUMERIC(38,0), where the server converts it exactly.
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Firebird", new fakeDbFactory(SupportedDatabase.Firebird));
        var gateway = new TableGateway<WideIntegerSelectEntity, int>(context);

        using var sc = gateway.BuildCreate(new WideIntegerSelectEntity { Id = 1, N = BigInteger.Parse("123456789012345678901234567890") });

        Assert.Contains("CAST(CAST(", sc.Query.ToString());
        Assert.Contains("AS VARCHAR(100)) AS NUMERIC(38,0))", sc.Query.ToString());
    }
}
