using System;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectParameterPoolingTests
{
    [Fact]
    public void CreateDbParameter_ReusedParametersClearProviderSpecificMetadata()
    {
        var dialect = new TestDialect(new FakeProviderFactory());

        var first = dialect.CreateDbParameter("p0", DbType.Guid, Guid.NewGuid());
        var firstProvider = Assert.IsType<FakeProviderParameter>(first);
        Assert.Equal(27, firstProvider.NpgsqlDbType);
        Assert.Equal("uuid", firstProvider.DataTypeName);

        dialect.ReturnParameterToPool(first);

        var second = dialect.CreateDbParameter("p1", DbType.DateTime, DateTime.UtcNow);
        var secondProvider = Assert.IsType<FakeProviderParameter>(second);

        Assert.Same(first, second);
        Assert.Equal(0, secondProvider.NpgsqlDbType);
        Assert.Null(secondProvider.DataTypeName);
    }

    private sealed class TestDialect : SqlDialect
    {
        public TestDialect(DbProviderFactory factory)
            : base(factory, NullLogger<SqlDialect>.Instance)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.PostgreSql;

        public override DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
        {
            var parameter = base.CreateDbParameter(name, type, value);

            if (parameter is FakeProviderParameter providerParam && type == DbType.Guid)
            {
                providerParam.NpgsqlDbType = 27;
                providerParam.DataTypeName = "uuid";
            }

            return parameter;
        }
    }

    private sealed class FakeProviderFactory : DbProviderFactory
    {
        public override DbParameter CreateParameter()
        {
            return new FakeProviderParameter();
        }
    }

    private sealed class FakeProviderParameter : fakeDbParameter
    {
        public int NpgsqlDbType { get; set; }
        public string? DataTypeName { get; set; }
    }
}
