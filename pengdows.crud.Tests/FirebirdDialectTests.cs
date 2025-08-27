using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class FirebirdDialectTests
{
    [Fact]
    public void QuotePrefixSuffix_AreDoubleQuotes()
    {
        var dialect = new FirebirdDialect(new FakeDbFactory(SupportedDatabase.Firebird), NullLogger<FirebirdDialect>.Instance);
        Assert.Equal("\"", dialect.QuotePrefix);
        Assert.Equal("\"", dialect.QuoteSuffix);
    }

    [Fact]
    public void CreateDbParameter_BooleanMapsToInt16()
    {
        var dialect = new FirebirdDialect(new FakeDbFactory(SupportedDatabase.Firebird), NullLogger<FirebirdDialect>.Instance);
        var paramTrue = dialect.CreateDbParameter("p", DbType.Boolean, true);
        Assert.Equal(DbType.Int16, paramTrue.DbType);
        Assert.Equal((short)1, paramTrue.Value);

        var paramFalse = dialect.CreateDbParameter("p", DbType.Boolean, false);
        Assert.Equal(DbType.Int16, paramFalse.DbType);
        Assert.Equal((short)0, paramFalse.Value);

        Assert.False(dialect.SupportsJsonTypes);
    }

    [Fact]
    public void ApplyConnectionSettings_WithScript_ExecutesCommand()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Firebird);
        var conn = (FakeDbConnection)factory.CreateConnection();
        conn.Open();
        conn.EnqueueNonQueryResult(1);
        var dialect = new FirebirdDialect(factory, NullLogger<FirebirdDialect>.Instance);
        dialect.ApplyConnectionSettings(conn);
        Assert.Empty(conn.NonQueryResults);
    }

    [Fact]
    public void ApplyConnectionSettings_WithSettings_ExecutesCommand()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Firebird);
        var conn = (FakeDbConnection)factory.CreateConnection();
        conn.Open();
        conn.EnqueueNonQueryResult(1);
        var dialect = new FirebirdDialect(factory, NullLogger<FirebirdDialect>.Instance);
        dialect.ApplyConnectionSettings(conn);
        Assert.Empty(conn.NonQueryResults);
    }

}
