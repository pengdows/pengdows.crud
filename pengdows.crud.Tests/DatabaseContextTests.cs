
#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class DatabaseContextTests
{
    public static IEnumerable<object[]> AllSupportedProviders()
    {
        return Enum.GetValues<SupportedDatabase>()
            .Where(p => p != SupportedDatabase.Unknown)
            .Select(p => new object[] { p });
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void CanInitializeContext_ForEachSupportedProvider(SupportedDatabase product)
    {
        var factory = new FakeDbFactory(product);
        var config = new DatabaseContextConfiguration
        {
            DbMode = DbMode.SingleWriter,
            ProviderName = product.ToString(),
            ConnectionString = $"Data Source=test;EmulatedProduct={product}"
        };
        var context = new DatabaseContext(config, factory);

        var conn = context.GetConnection(ExecutionType.Read);
        Assert.NotNull(conn);
        Assert.Equal(ConnectionState.Closed, conn.State);

        var schema = conn.GetSchema();
        Assert.NotNull(schema);
        Assert.True(schema.Rows.Count > 0);
    }

    // [Fact]
    // public void Constructor_WithNullFactory_Throws()
    // {
    //     Assert.Throws<NullReferenceException>(() =>
    //         new DatabaseContext("fake", (string)null!));
    // }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void WrapObjectName_SplitsAndWrapsCorrectly(SupportedDatabase product)
    {
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var wrapped = context.WrapObjectName("schema.table");
        Assert.Contains(".", wrapped);
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void GenerateRandomName_ValidatesFirstChar(SupportedDatabase product)
    {
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var name = context.GenerateRandomName(10);
        Assert.True(char.IsLetter(name[0]));
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void CreateDbParameter_SetsPropertiesCorrectly(SupportedDatabase product)
    {
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var result = context.CreateDbParameter("p1", DbType.Int32, 123);

        Assert.Equal("p1", result.ParameterName);
        Assert.Equal(DbType.Int32, result.DbType);
        Assert.Equal(123, result.Value);
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public async Task CloseAndDisposeConnectionAsync_WithAsyncDisposable_DisposesCorrectly(SupportedDatabase product)
    {
        var mockTracked = new Mock<ITrackedConnection>();
        mockTracked.As<IAsyncDisposable>().Setup(d => d.DisposeAsync())
            .Returns(ValueTask.CompletedTask).Verifiable();

        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        await context.CloseAndDisposeConnectionAsync(mockTracked.Object);

        mockTracked.As<IAsyncDisposable>().Verify(d => d.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task CloseAndDisposeConnectionAsync_Null_DoesNothing()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        await context.CloseAndDisposeConnectionAsync(null);
    }

    [Fact]
    public void CloseAndDisposeConnection_Null_DoesNothing()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        context.CloseAndDisposeConnection(null);
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void AssertIsWriteConnection_WhenFalse_Throws(SupportedDatabase product)
    {
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory,
            readWriteMode: ReadWriteMode.ReadOnly);
        Assert.Throws<InvalidOperationException>(() => context.AssertIsWriteConnection());
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void AssertIsReadConnection_WhenFalse_Throws(SupportedDatabase product)
    {
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory,
            readWriteMode: ReadWriteMode.WriteOnly);
        Assert.Throws<InvalidOperationException>(() => context.AssertIsReadConnection());
    }

    public static IEnumerable<object[]> ProvidersWithSettings()
    {
        return new List<object[]>
        {
            new object[] { SupportedDatabase.SqlServer, false },
            new object[] { SupportedDatabase.MySql, true },
            new object[] { SupportedDatabase.MariaDb, true },
            new object[] { SupportedDatabase.PostgreSql, true },
            new object[] { SupportedDatabase.CockroachDb, true },
            new object[] { SupportedDatabase.Oracle, true },
            new object[] { SupportedDatabase.Sqlite, true },
            new object[] { SupportedDatabase.Firebird, false }
        };
    }

    [Theory]
    [MemberData(nameof(ProvidersWithSettings))]
    public void SessionSettingsPreamble_CorrectPerProvider(SupportedDatabase product, bool expectSettings)
    {
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var preamble = context.SessionSettingsPreamble;
        if (expectSettings)
            Assert.False(string.IsNullOrWhiteSpace(preamble));
        else
            Assert.True(string.IsNullOrWhiteSpace(preamble));
    }

    [Fact]
    public void CloseAndDisposeConnection_StandardMode_ClosesConnection()
    {
        var product = SupportedDatabase.SqlServer;
        var factory = new FakeDbFactory(product);
        var context =
            new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory, mode: DbMode.Standard);
        Assert.Equal(DbMode.Standard, context.ConnectionMode);
        var conn = context.GetConnection(ExecutionType.Read);
        conn.Open();
        Assert.Equal(1, context.NumberOfOpenConnections);
        context.CloseAndDisposeConnection(conn);
        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Fact]
    public void CloseAndDisposeConnection_SingleConnectionMode_KeepsOpen()
    {
        var product = SupportedDatabase.Sqlite;
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            DbMode = DbMode.SingleConnection
        };
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext(config, factory);
        Assert.Equal(DbMode.SingleConnection, context.ConnectionMode);
        var conn = context.GetConnection(ExecutionType.Read);
        Assert.Equal(ConnectionState.Open, conn.State);
        var before = context.NumberOfOpenConnections;
        context.CloseAndDisposeConnection(conn);
        Assert.Equal(before, context.NumberOfOpenConnections);
        Assert.Equal(ConnectionState.Open, conn.State);
        context.Dispose();
        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Fact]
    public void BeginTransaction_ReadOnly_InvalidIsolation_Throws()
    {
        var product = SupportedDatabase.Sqlite;
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        Assert.Throws<InvalidOperationException>(
            () => context.BeginTransaction(IsolationLevel.ReadCommitted, ExecutionType.Read));
    }

    [Fact]
    public void BeginTransaction_WriteOnReadOnlyContext_Throws()
    {
        var product = SupportedDatabase.Sqlite;
        var factory = new FakeDbFactory(product);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        var context = new DatabaseContext(config, factory);
        Assert.Throws<NotSupportedException>(() => context.BeginTransaction(executionType: ExecutionType.Write));
    }

    [Fact]
    public void BeginTransaction_ReadOnly_SingleWriter_DisposesEphemeralConnection()
    {
        var product = SupportedDatabase.Sqlite;
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            DbMode = DbMode.SingleWriter
        };
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext(config, factory);
        Assert.Equal(1, context.NumberOfOpenConnections);

        using (context.BeginTransaction(executionType: ExecutionType.Read))
        {
            Assert.Equal(1, context.NumberOfOpenConnections);
        }

        Assert.Equal(1, context.NumberOfOpenConnections);
    }

    [Fact]
    public void MaxNumberOfConnections_TracksPeakUsage()
    {
        var product = SupportedDatabase.SqlServer;
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var c1 = context.GetConnection(ExecutionType.Read);
        var c2 = context.GetConnection(ExecutionType.Read);
        c1.Open();
        c2.Open();
        Assert.Equal(2, context.MaxNumberOfConnections);
        context.CloseAndDisposeConnection(c1);
        context.CloseAndDisposeConnection(c2);
        Assert.Equal(2, context.MaxNumberOfConnections);
    }

    [Fact]
    public void RCSIEnabled_DefaultIsFalse()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}", factory);
        Assert.False(context.RCSIEnabled);
    }

    [Fact]
    public void MakeParameterName_UsesDatabaseMarker()
    {
        var product = SupportedDatabase.PostgreSql;
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var name = context.MakeParameterName("foo");
        Assert.StartsWith(context.DataSourceInfo.ParameterMarker, name);
    }

    [Theory]
    [InlineData(DbMode.Standard)]
    [InlineData(DbMode.KeepAlive)]
    [InlineData(DbMode.SingleConnection)]
    [InlineData(DbMode.SingleWriter)]
    public void GetLock_ReturnsNoOpAsyncLocker(DbMode mode)
    {
        var product = SupportedDatabase.Sqlite;
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            DbMode = mode
        };
        var factory = new FakeDbFactory(product);
        using var context = new DatabaseContext(config, factory);

        var first = context.GetLock();
        var second = context.GetLock();

        Assert.IsType<NoOpAsyncLocker>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void GetLock_WhenDisposed_Throws()
    {
        var product = SupportedDatabase.Sqlite;
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            DbMode = DbMode.Standard
        };
        var factory = new FakeDbFactory(product);
        var context = new DatabaseContext(config, factory);
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.GetLock());
    }

    [Fact]
    public void PinnedConnection_AppliesSessionSettings()
    {
        var factory = new RecordingFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.SingleConnection
        };

        _ = new DatabaseContext(config, factory);

        Assert.Contains("PRAGMA foreign_keys = ON;", factory.Connection.ExecutedCommands);
    }

    [Fact]
    public void PinnedConnection_WithoutSessionSettings_DoesNotExecute()
    {
        var factory = new RecordingFactory(SupportedDatabase.Firebird);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Firebird",
            ProviderName = SupportedDatabase.Firebird.ToString(),
            DbMode = DbMode.SingleConnection
        };

        _ = new DatabaseContext(config, factory);

        Assert.Empty(factory.Connection.ExecutedCommands);
    }

    private sealed class RecordingFactory : DbProviderFactory
    {
        public RecordingConnection Connection { get; }

        public RecordingFactory(SupportedDatabase product)
        {
            Connection = new RecordingConnection { EmulatedProduct = product };
        }

        public override DbConnection CreateConnection()
        {
            return Connection;
        }

        public override DbCommand CreateCommand()
        {
            return new FakeDbCommand();
        }

        public override DbParameter CreateParameter()
        {
            return new FakeDbParameter();
        }
    }

    private sealed class RecordingConnection : FakeDbConnection
    {
        public List<string> ExecutedCommands { get; } = new();

        protected override DbCommand CreateDbCommand()
        {
            return new RecordingCommand(this, ExecutedCommands);
        }
    }

    private sealed class RecordingCommand : FakeDbCommand
    {
        private readonly List<string> _record;

        public RecordingCommand(FakeDbConnection connection, List<string> record) : base(connection)
        {
            _record = record;
        }

        public override int ExecuteNonQuery()
        {
            _record.Add(CommandText);
            return base.ExecuteNonQuery();
        }
    }
}
