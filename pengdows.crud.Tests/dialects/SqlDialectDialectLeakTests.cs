using System;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.Tests.Mocks;
using Xunit;

namespace pengdows.crud.Tests.dialects;

public class SqlDialectDialectLeakTests
{
    [Fact]
    public void UpsertIncomingColumn_ThrowsForBaseDialect()
    {
        var dialect = new TestSql92Dialect(new NullParameterFactory());

        var ex = Assert.Throws<NotSupportedException>(() => dialect.UpsertIncomingColumn("col"));

        Assert.Contains("UpsertIncomingColumn", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FirebirdNaturalKeyLookup_UsesRows1()
    {
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird),
            NullLogger<FirebirdDialect>.Instance);
        var sql = dialect.GetNaturalKeyLookupQuery("t", "id", new[] { "a", "b" }, new[] { "@a", "@b" });

        Assert.Contains("ROWS 1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OracleNaturalKeyLookup_UsesFetchFirstRowsOnly()
    {
        var dialect =
            new OracleDialect(new fakeDbFactory(SupportedDatabase.Oracle), NullLogger<OracleDialect>.Instance);
        var sql = dialect.GetNaturalKeyLookupQuery("t", "id", new[] { "a", "b" }, new[] { ":a", ":b" });

        Assert.Contains("FETCH FIRST 1 ROWS ONLY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ROWNUM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestSql92Dialect : Sql92Dialect
    {
        public TestSql92Dialect(DbProviderFactory factory)
            : base(factory, NullLogger<Sql92Dialect>.Instance)
        {
        }
    }
}