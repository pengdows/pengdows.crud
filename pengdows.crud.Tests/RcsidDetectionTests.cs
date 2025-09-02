using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class RcsidDetectionTests
{
    private static ITrackedConnection BuildConnection(int rcsiFlag)
    {
        var inner = new fakeDbConnection
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={SupportedDatabase.SqlServer}"
        };
        inner.EnqueueScalarResult(rcsiFlag);
        inner.Open();
        return new TrackedConnection(inner);
    }

    [Fact]
    public void SqlServerDialect_ReturnsTrueWhenRCSIEnabled()
    {
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer),
            NullLoggerFactory.Instance.CreateLogger<SqlServerDialect>());
        var conn = BuildConnection(1);
        var result = dialect.IsReadCommittedSnapshotOn(conn);
        Assert.True(result);
    }

    [Fact]
    public void SqlServerDialect_ReturnsFalseWhenRCSIDisabled()
    {
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer),
            NullLoggerFactory.Instance.CreateLogger<SqlServerDialect>());
        var conn = BuildConnection(0);
        var result = dialect.IsReadCommittedSnapshotOn(conn);
        Assert.False(result);
    }

    [Fact]
    public void Sql92Dialect_RCSICheckAlwaysFalse()
    {
        var dialect = new Sql92Dialect(new fakeDbFactory(SupportedDatabase.Unknown),
            NullLoggerFactory.Instance.CreateLogger<Sql92Dialect>());
        var conn = BuildConnection(1);
        var result = dialect.IsReadCommittedSnapshotOn(conn);
        Assert.False(result);
    }

    private class RcsiDbFactory : DbProviderFactory
    {
        private readonly int _flag;

        public RcsiDbFactory(int flag)
        {
            _flag = flag;
        }

        public override DbConnection CreateConnection()
        {
            var conn = new fakeDbConnection
            {
                ConnectionString = $"Data Source=:memory:;EmulatedProduct={SupportedDatabase.SqlServer}"
            };
            conn.EnqueueScalarResult(_flag);
            return conn;
        }

        public override DbCommand CreateCommand() => new fakeDbCommand();
        public override DbParameter CreateParameter() => new fakeDbParameter();
    }

    [Fact]
    public void DatabaseContext_RCSIEnabledTrueWhenDetected()
    {
        var factory = new RcsiDbFactory(1);
        using var context = new DatabaseContext($"Data Source=:memory:;EmulatedProduct={SupportedDatabase.SqlServer}", factory);
        Assert.True(context.RCSIEnabled);
    }

    [Fact]
    public void DatabaseContext_RCSIEnabledFalseWhenDisabled()
    {
        var factory = new RcsiDbFactory(0);
        using var context = new DatabaseContext($"Data Source=:memory:;EmulatedProduct={SupportedDatabase.SqlServer}", factory);
        Assert.False(context.RCSIEnabled);
    }
}
