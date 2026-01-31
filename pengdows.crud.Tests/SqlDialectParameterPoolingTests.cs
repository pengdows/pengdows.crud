using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectParameterPoolingTests
{
    [Fact]
    public void SqlServerParameterReuse_AfterCommandDisposed_DetachesParent()
    {
        var dialect = new SqlServerDialect(SqlClientFactory.Instance, NullLogger.Instance);
        var param = dialect.CreateDbParameter("i1", DbType.String, "first");

        using (var cmd = new SqlCommand())
        {
            cmd.Parameters.Add(param);
            cmd.Parameters.Clear();
        }

        dialect.ReturnParameterToPool(param);

        var reused = dialect.CreateDbParameter("i1", DbType.String, "second");
        Assert.Same(param, reused);

        using var cmd2 = new SqlCommand();
        var ex = Record.Exception(() => cmd2.Parameters.Add(reused));
        Assert.Null(ex);
    }

    [Fact]
    public void SqlServerParameterReuse_AfterCommandDisposedWithoutClear_DetachesParent()
    {
        var dialect = new SqlServerDialect(SqlClientFactory.Instance, NullLogger.Instance);
        var param = dialect.CreateDbParameter("i1", DbType.String, "first");

        using (var cmd = new SqlCommand())
        {
            cmd.Parameters.Add(param);
        }

        dialect.ReturnParameterToPool(param);

        var reused = dialect.CreateDbParameter("i1", DbType.String, "second");
        Assert.Same(param, reused);

        using var cmd2 = new SqlCommand();
        var ex = Record.Exception(() => cmd2.Parameters.Add(reused));
        Assert.Null(ex);
    }

    [Fact]
    public void SqliteParameterReuse_AfterCommandDisposed_DetachesParent()
    {
        var dialect = new SqliteDialect(SqliteFactory.Instance, NullLogger.Instance);
        var param = dialect.CreateDbParameter("i1", DbType.String, "first");

        using (var cmd = new SqliteCommand())
        {
            cmd.Parameters.Add(param);
            cmd.Parameters.Clear();
        }

        dialect.ReturnParameterToPool(param);

        var reused = dialect.CreateDbParameter("i1", DbType.String, "second");
        Assert.Same(param, reused);

        using var cmd2 = new SqliteCommand();
        var ex = Record.Exception(() => cmd2.Parameters.Add(reused));
        Assert.Null(ex);
    }

    [Fact]
    public void SqliteParameterReuse_AfterCommandDisposedWithoutClear_DetachesParent()
    {
        var dialect = new SqliteDialect(SqliteFactory.Instance, NullLogger.Instance);
        var param = dialect.CreateDbParameter("i1", DbType.String, "first");

        using (var cmd = new SqliteCommand())
        {
            cmd.Parameters.Add(param);
        }

        dialect.ReturnParameterToPool(param);

        var reused = dialect.CreateDbParameter("i1", DbType.String, "second");
        Assert.Same(param, reused);

        using var cmd2 = new SqliteCommand();
        var ex = Record.Exception(() => cmd2.Parameters.Add(reused));
        Assert.Null(ex);
    }
}
