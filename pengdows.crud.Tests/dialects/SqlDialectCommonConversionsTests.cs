using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

public class SqlDialectCommonConversionsTests
{
    [Fact]
    public void CreateDbParameter_WithBooleanValue_UsesCommonConversion()
    {
        var dialect = new StubSqlDialect();
        var parameter = dialect.CreateDbParameter("p", DbType.Boolean, true);

        Assert.Equal(DbType.Int16, parameter.DbType);
        Assert.Equal((short)1, parameter.Value);
    }

    [Fact]
    public void CreateDbParameter_WithUnmappedType_DoesNotChangeDbType()
    {
        var dialect = new StubSqlDialect();
        var parameter = dialect.CreateDbParameter("p", DbType.String, "abc");

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("abc", parameter.Value);
    }

    private sealed class StubSqlDialect : SqlDialect
    {
        public StubSqlDialect()
            : base(fakeDbFactory.Instance, NullLogger.Instance)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;

        public override bool SupportsNamedParameters => false;
    }
}