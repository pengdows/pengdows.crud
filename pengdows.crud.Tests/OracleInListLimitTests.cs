using System;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Oracle before 23ai allows at most 1000 expressions in an IN list (ORA-01795). The id list is
/// padded to a power of two for statement-cache reuse, so anything over 512 ids became an IN list
/// of 1024 placeholders and failed on 12.2 through 21c.
/// </summary>
public class OracleInListLimitTests
{
    private const int OracleMaxInListExpressions = 1000;

    [Table("InListEntity")]
    public class InListEntity
    {
        [Id(true)]
        [Column("Id", DbType.Int64)]
        public long Id { get; set; }

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    private static DatabaseContext OracleContext() =>
        new("Data Source=test;EmulatedProduct=Oracle", new fakeDbFactory(SupportedDatabase.Oracle));

    private static int[] InListSizes(string sql) =>
        Regex.Matches(sql, @"\bIN \(([^)]*)\)")
            .Select(m => m.Groups[1].Value.Split(',').Length)
            .ToArray();

    [Theory]
    [InlineData(513)]
    [InlineData(1000)]
    [InlineData(2500)]
    public void BuildRetrieve_ManyIds_NoInListExceedsOraclesLimit(int count)
    {
        using var context = OracleContext();
        var gateway = new TableGateway<InListEntity, long>(context);
        var ids = Enumerable.Range(1, count).Select(i => (long)i).ToList();

        using var sc = gateway.BuildRetrieve(ids);
        var sizes = InListSizes(sc.Query.ToString());

        Assert.NotEmpty(sizes);
        Assert.All(sizes, size => Assert.InRange(size, 1, OracleMaxInListExpressions));
        Assert.True(sizes.Sum() >= count, $"IN lists hold {sizes.Sum()} placeholders for {count} ids");
    }

    [Fact]
    public void BuildRetrieve_ManyIdsWithNull_SplitListsStayInsideTheNullAlternative()
    {
        using var context = OracleContext();
        var gateway = new TableGateway<InListEntity, long?>(context);
        var ids = Enumerable.Range(1, 1500).Select(i => (long?)i).Append(null).ToList();

        using var sc = context.CreateSqlContainer("SELECT 1 FROM \"InListEntity\"");
        gateway.BuildWhere(context.WrapObjectName("Id"), ids, sc);
        var sql = sc.Query.ToString();
        var where = sql[(sql.IndexOf(" WHERE ", StringComparison.Ordinal) + 7)..];

        Assert.All(InListSizes(sql), size => Assert.InRange(size, 1, OracleMaxInListExpressions));
        // The whole predicate is one parenthesized group: (... IN (...) OR ... IN (...) OR ... IS NULL)
        Assert.StartsWith("(", where);
        Assert.EndsWith("IS NULL)", where.TrimEnd());
    }

    [Fact]
    public void BuildDelete_ManyIds_NoInListExceedsOraclesLimit()
    {
        using var context = OracleContext();
        var gateway = new TableGateway<InListEntity, long>(context);
        var ids = Enumerable.Range(1, 1000).Select(i => (long)i).ToList();

        var sizes = gateway.BuildBatchDelete(ids).SelectMany(sc => InListSizes(sc.Query.ToString())).ToArray();

        Assert.NotEmpty(sizes);
        Assert.All(sizes, size => Assert.InRange(size, 1, OracleMaxInListExpressions));
    }
}
