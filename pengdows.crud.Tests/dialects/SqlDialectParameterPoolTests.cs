using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

public sealed class SqlDialectParameterPoolTests
{
    [Fact]
    public void ParameterReturnedToPoolIsReusedWithCleanState()
    {
        var dialect = CreateDialect();
        var first = dialect.CreateDbParameter("p0", DbType.String, "initial");
        first.Direction = ParameterDirection.Output;
        first.Size = 128;
        first.Precision = 4;
        first.Scale = 2;
        first.Value = "custom";

        dialect.ReturnParameterToPool(first);

        var second = dialect.CreateDbParameter("p1", DbType.Int32, 5);

        Assert.Same(first, second);
        Assert.Equal(ParameterDirection.Input, second.Direction);
        Assert.Equal(0, second.Size);
        Assert.Equal(0, second.Precision);
        Assert.Equal(0, second.Scale);
        Assert.Equal(DbType.Int32, second.DbType);
        Assert.Equal(5, second.Value);
        Assert.Equal("p1", second.ParameterName);

        dialect.ReturnParameterToPool(second);
    }

    [Fact]
    public void ParameterPoolRespectsMaxSize()
    {
        var dialect = CreateDialect();
        var parameters = new List<DbParameter>();

        for (var i = 0; i < 150; i++)
        {
            var parameter = dialect.CreateDbParameter($"p{i}", DbType.Int32, i);
            parameters.Add(parameter);
        }

        for (var i = 0; i < parameters.Count; i++)
        {
            dialect.ReturnParameterToPool(parameters[i]);
        }

        Assert.Equal(100, dialect.PoolCount);
    }

    private static TestDialect CreateDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        return new TestDialect(factory, NullLogger.Instance);
    }

    private sealed class TestDialect : SqlDialect
    {
        private readonly ConcurrentQueue<DbParameter> _queue;

        public TestDialect(DbProviderFactory factory, ILogger logger)
            : base(factory, logger)
        {
            var field = typeof(SqlDialect).GetField("_parameterPool", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                throw new InvalidOperationException("Parameter pool field not found.");
            }

            _queue = (ConcurrentQueue<DbParameter>?)field.GetValue(this) ?? throw new InvalidOperationException("Pool not initialized.");
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.SqlServer;

        public int PoolCount => _queue.Count;
    }
}
