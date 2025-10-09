
#region

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.isolation;
using pengdows.crud.Tests.Mocks;
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
        var factory = new fakeDbFactory(product);
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
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var wrapped = context.WrapObjectName("schema.table");
        Assert.Contains(".", wrapped);
    }

    [Fact]
    public void WrapObjectName_Null_ReturnsEmpty()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        var result = context.WrapObjectName(null);
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void QuoteProperties_DelegateToDialect(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        Assert.Equal(context.DataSourceInfo.QuotePrefix, context.QuotePrefix);
        Assert.Equal(context.DataSourceInfo.QuoteSuffix, context.QuoteSuffix);
        Assert.Equal(context.DataSourceInfo.CompositeIdentifierSeparator, context.CompositeIdentifierSeparator);
        Assert.NotEqual("?", context.QuotePrefix);
        Assert.NotEqual("?", context.QuoteSuffix);
        Assert.NotEqual("?", context.CompositeIdentifierSeparator);
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void GenerateRandomName_ValidatesFirstChar(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var name = context.GenerateRandomName(10);
        Assert.True(char.IsLetter(name[0]));
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void CreateDbParameter_SetsPropertiesCorrectly(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var result = context.CreateDbParameter("p1", DbType.Int32, 123, ParameterDirection.Output);

        Assert.Equal("p1", result.ParameterName);
        Assert.Equal(DbType.Int32, result.DbType);
        Assert.Equal(123, result.Value);
        Assert.Equal(ParameterDirection.Output, result.Direction);
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void CreateDbParameter_DefaultsDirectionToInput(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var result = context.CreateDbParameter("p1", DbType.Int32, 123);

        Assert.Equal(ParameterDirection.Input, result.Direction);
    }

    [Theory]
    [InlineData("@foo", "foo")]
    [InlineData(":bar", "bar")]
    [InlineData("?baz", "baz")]
    public void CreateDbParameter_RemovesPrefixesFromName(string input, string expected)
    {
        var product = SupportedDatabase.Sqlite;
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var result = context.CreateDbParameter(input, DbType.String, "v");

        Assert.Equal(expected, result.ParameterName);
    }

    [Fact]
    public void CreateDbParameter_FactoryReturnsNull_Throws()
    {
        var factory = new NullParameterFactory();
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);

        Assert.Throws<InvalidOperationException>(() => context.CreateDbParameter("p", DbType.Int32, 1));
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public async Task CloseAndDisposeConnectionAsync_WithAsyncDisposable_DisposesCorrectly(SupportedDatabase product)
    {
        var mockTracked = new Mock<ITrackedConnection>();
        mockTracked.As<IAsyncDisposable>().Setup(d => d.DisposeAsync())
            .Returns(ValueTask.CompletedTask).Verifiable();

        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        await context.CloseAndDisposeConnectionAsync(mockTracked.Object);

        mockTracked.As<IAsyncDisposable>().Verify(d => d.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task CloseAndDisposeConnectionAsync_Null_DoesNothing()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        await context.CloseAndDisposeConnectionAsync(null);
    }

    [Fact]
    public void CloseAndDisposeConnection_Null_DoesNothing()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        context.CloseAndDisposeConnection(null);
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void AssertIsWriteConnection_WhenFalse_Throws(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory,
            readWriteMode: ReadWriteMode.ReadOnly);
        Assert.Throws<InvalidOperationException>(() => context.AssertIsWriteConnection());
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void AssertIsReadConnection_WhenFalse_Throws(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory,
            readWriteMode: 0);
        Assert.Throws<InvalidOperationException>(() => context.AssertIsReadConnection());
    }

    public static IEnumerable<object[]> ProvidersWithSettings()
    {
        return new List<object[]>
        {
            new object[] { SupportedDatabase.SqlServer, true },
            new object[] { SupportedDatabase.MySql, true },
            new object[] { SupportedDatabase.MariaDb, true },
            new object[] { SupportedDatabase.PostgreSql, true },
            new object[] { SupportedDatabase.CockroachDb, true },
            new object[] { SupportedDatabase.Oracle, true },
            new object[] { SupportedDatabase.Sqlite, true },
            new object[] { SupportedDatabase.Firebird, true },
            new object[] { SupportedDatabase.DuckDB, false }
        };
    }

    [Theory]
    [MemberData(nameof(ProvidersWithSettings))]
    public void SessionSettingsPreamble_CorrectPerProvider(SupportedDatabase product, bool expectSettings)
    {
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var preamble = context.SessionSettingsPreamble;
        if (expectSettings)
        {
            Assert.False(string.IsNullOrWhiteSpace(preamble));
        }
        else
        {
            Assert.True(string.IsNullOrWhiteSpace(preamble));
        }
    }

    [Fact]
    public void CloseAndDisposeConnection_StandardMode_ClosesConnection()
    {
        var product = SupportedDatabase.SqlServer;
        var factory = new fakeDbFactory(product);
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
        var factory = new fakeDbFactory(product);
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
    public void DuckDBInMemory_SetsSingleConnectionMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        using var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=DuckDB", factory);
        Assert.Equal(DbMode.SingleConnection, context.ConnectionMode);
    }

    [Fact]
    public void DuckDBFile_SetsSingleWriterMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=DuckDB", factory);
        Assert.Equal(DbMode.SingleWriter, context.ConnectionMode);
    }

    [Fact]
    public void BeginTransaction_ReadOnly_DefaultsToResolverLevel()
    {
        var product = SupportedDatabase.Sqlite;
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        using var tx = context.BeginTransaction(readOnly: true);
        var expected = new IsolationResolver(product, context.RCSIEnabled, context.SnapshotIsolationEnabled)
            .Resolve(IsolationProfile.SafeNonBlockingReads);
        Assert.Equal(expected, tx.IsolationLevel);
    }

    [Fact]
    public void SnapshotIsolationEnabled_PrefetchRecognizesEnabledState()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetIdPopulationResult(null);

        foreach (var connection in factory.Connections)
        {
            connection.ScalarResultsByCommand["SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE name = DB_NAME()"] = 1;
            connection.ScalarResultsByCommand["SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()"] = 1;
        }

        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);

        Assert.True(context.RCSIEnabled);
        Assert.True(context.SnapshotIsolationEnabled);
    }

    [Fact]
    public void SnapshotIsolationEnabled_FalseWhenDatabaseDisablesSnapshotIsolation()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetIdPopulationResult(null);

        foreach (var connection in factory.Connections)
        {
            connection.ScalarResultsByCommand["SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE name = DB_NAME()"] = 1;
            connection.ScalarResultsByCommand["SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()"] = 0;
        }

        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);

        Assert.True(context.RCSIEnabled);
        Assert.False(context.SnapshotIsolationEnabled);
    }

    [Fact]
    public void BeginTransaction_WriteOnReadOnlyContext_Throws()
    {
        var product = SupportedDatabase.Sqlite;
        var factory = new fakeDbFactory(product);
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
    public void BeginTransaction_ReadOnly_UnsupportedIsolation_Throws()
    {
        var product = SupportedDatabase.Sqlite;
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        Assert.Throws<InvalidOperationException>(
            () => context.BeginTransaction(IsolationLevel.Snapshot, ExecutionType.Read));
    }

    [Fact]
    public void MaxNumberOfConnections_TracksPeakUsage()
    {
        var product = SupportedDatabase.SqlServer;
        var factory = new fakeDbFactory(product);
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
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}", factory);
        Assert.False(context.RCSIEnabled);
    }

    [Fact]
    public void MakeParameterName_UsesDatabaseMarker()
    {
        var product = SupportedDatabase.PostgreSql;
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var name = context.MakeParameterName("foo");
        var expected = context.DataSourceInfo.ParameterMarker + "foo";
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData("@foo")]
    [InlineData(":foo")]
    [InlineData("?foo")]
    [InlineData("@:foo?")]
    public void MakeParameterName_StripsExistingPrefixes(string input)
    {
        var product = SupportedDatabase.Sqlite;
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var name = context.MakeParameterName(input);
        var expected = context.DataSourceInfo.ParameterMarker + "foo";
        Assert.Equal(expected, name);
    }

    [Fact]
    public void MakeParameterName_DbParameter_StripsPrefixes()
    {
        var product = SupportedDatabase.Sqlite;
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var param = new fakeDbParameter { ParameterName = ":foo", DbType = DbType.String, Value = "x" };

        var name = context.MakeParameterName(param);

        Assert.Equal(context.DataSourceInfo.ParameterMarker + "foo", name);
    }


    [Fact]
    public void MaxOutputParameters_ExposedViaContext()
    {
        var product = SupportedDatabase.SqlServer;
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);

        Assert.Equal(context.DataSourceInfo.MaxOutputParameters, context.MaxOutputParameters);

    }

    [Fact]
    public void Product_WhenInitialized_ReturnsProvidedProduct()
    {
        var product = SupportedDatabase.SqlServer;
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);

        Assert.Equal(product, context.Product);
    }

    [Fact]
    public void Product_WithoutDataSourceInfo_ReturnsUnknown()
    {
        var context = (DatabaseContext)FormatterServices.GetUninitializedObject(typeof(DatabaseContext));

        Assert.Equal(SupportedDatabase.Unknown, context.Product);

    }

    [Fact]
    public void MakeParameterName_DbParameter_UsesMarker()
    {
        var product = SupportedDatabase.PostgreSql;
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        var p = context.CreateDbParameter("p", DbType.Int32, 1);
        var name = context.MakeParameterName(p);
        Assert.StartsWith(context.DataSourceInfo.ParameterMarker, name);
    }

    [Fact]
    public void MakeParameterName_NullString_ReturnsMarker()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", factory);
        Assert.Equal(context.DataSourceInfo.ParameterMarker, context.MakeParameterName((string)null));
    }

    [Fact]
    public void MakeParameterName_NullParameter_Throws()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        Assert.Throws<NullReferenceException>(() => context.MakeParameterName((DbParameter)null!));
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
        var factory = new fakeDbFactory(product);
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
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext(config, factory);
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.GetLock());
    }

    [Fact]
    public void StandardConnection_DoesNotApplySessionSettings()
    {
        var factory = new RecordingFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.Standard
        };

        _ = new DatabaseContext(config, factory);

        // Should execute DBCC USEROPTIONS to check settings, but no SET commands since settings are already correct
        Assert.Contains("DBCC USEROPTIONS", factory.Connection.ExecutedCommands);
        Assert.DoesNotContain(factory.Connection.ExecutedCommands, cmd => cmd.Contains("SET"));
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

        Assert.Contains("PRAGMA foreign_keys = ON", factory.Connection.ExecutedCommands);
    }

    [Fact]
    public void PinnedConnection_WithoutSessionSettings_DoesNotExecute()
    {
        var factory = new RecordingFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.SingleConnection
        };

        _ = new DatabaseContext(config, factory);

        // Should execute DBCC USEROPTIONS to check settings, but no SET commands since settings are already correct
        Assert.Contains("DBCC USEROPTIONS", factory.Connection.ExecutedCommands);
        Assert.DoesNotContain(factory.Connection.ExecutedCommands, cmd => cmd.Contains("SET"));
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
            return new fakeDbCommand();
        }

        public override DbParameter CreateParameter()
        {
            return new fakeDbParameter();
        }
    }

    private sealed class RecordingConnection : fakeDbConnection
    {
        public List<string> ExecutedCommands { get; } = new();

        protected override DbCommand CreateDbCommand()
        {
            return new RecordingCommand(this, ExecutedCommands);
        }
    }

    private sealed class RecordingCommand : fakeDbCommand
    {
        private readonly List<string> _record;

        public RecordingCommand(fakeDbConnection connection, List<string> record) : base(connection)
        {
            _record = record;
        }

        public override int ExecuteNonQuery()
        {
            _record.Add(CommandText);
            return base.ExecuteNonQuery();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            _record.Add(CommandText);
            
            // Mock DBCC USEROPTIONS to return correct settings so SqlServerDialect doesn't generate session settings
            if (CommandText.Trim().Equals("DBCC USEROPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                return new SqlServerSettingsDataReader();
            }
            
            return base.ExecuteDbDataReader(behavior);
        }
    }

    private sealed class SqlServerSettingsDataReader : DbDataReader
    {
        private readonly List<(string Setting, string Value)> _settings = new()
        {
            ("ANSI_NULLS", "SET"),
            ("ANSI_PADDING", "SET"), 
            ("ANSI_WARNINGS", "SET"),
            ("ARITHABORT", "SET"),
            ("CONCAT_NULL_YIELDS_NULL", "SET"),
            ("QUOTED_IDENTIFIER", "SET"),
            ("NUMERIC_ROUNDABORT", "NOT SET")
        };
        
        private int _index = -1;
        
        public override bool GetBoolean(int ordinal) => throw new InvalidOperationException();
        public override byte GetByte(int ordinal) => throw new InvalidOperationException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new InvalidOperationException();
        public override char GetChar(int ordinal) => throw new InvalidOperationException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new InvalidOperationException();
        public override string GetDataTypeName(int ordinal) => throw new InvalidOperationException();
        public override DateTime GetDateTime(int ordinal) => throw new InvalidOperationException();
        public override decimal GetDecimal(int ordinal) => throw new InvalidOperationException();
        public override double GetDouble(int ordinal) => throw new InvalidOperationException();
        public override Type GetFieldType(int ordinal) => throw new InvalidOperationException();
        public override float GetFloat(int ordinal) => throw new InvalidOperationException();
        public override Guid GetGuid(int ordinal) => throw new InvalidOperationException();
        public override short GetInt16(int ordinal) => throw new InvalidOperationException();
        public override int GetInt32(int ordinal) => throw new InvalidOperationException();
        public override long GetInt64(int ordinal) => throw new InvalidOperationException();
        public override string GetName(int ordinal) => throw new InvalidOperationException();
        public override int GetOrdinal(string name) => throw new InvalidOperationException();
        
        public override string GetString(int ordinal)
        {
            if (_index < 0 || _index >= _settings.Count)
                throw new InvalidOperationException();
                
            return ordinal switch
            {
                0 => _settings[_index].Setting,
                1 => _settings[_index].Value,
                _ => throw new InvalidOperationException()
            };
        }
        
        public override object GetValue(int ordinal) => GetString(ordinal);
        public override int GetValues(object[] values) => throw new InvalidOperationException();
        public override bool IsDBNull(int ordinal) => false;
        public override int FieldCount => 2;
        public override object this[int ordinal] => GetString(ordinal);
        public override object this[string name] => throw new InvalidOperationException();
        public override int RecordsAffected => 0;
        public override bool HasRows => _settings.Count > 0;
        public override bool IsClosed => false;
        public override bool NextResult() => false;
        public override bool Read() 
        {
            _index++;
            return _index < _settings.Count;
        }
        public override int Depth => 0;
        public override IEnumerator GetEnumerator() => ((IEnumerable)_settings).GetEnumerator();
    }

    [Fact]
    public void Constructor_StandardMode_DisposesInitializationConnection()
    {
        var factory = new DisposalTrackingFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.Standard
        };

        // Create DatabaseContext which should dispose the initialization connection
        using var context = new DatabaseContext(config, factory);

        // Verify that the initialization connection was disposed
        Assert.True(factory.InitializationConnection.WasDisposed, 
            "Initialization connection should be disposed after DatabaseContext construction in Standard mode");
        
        // Verify context was created successfully despite connection disposal
        Assert.Equal(DbMode.Standard, context.ConnectionMode);
        Assert.Equal(SupportedDatabase.SqlServer, context.Product);
    }

    [Fact]
    public void Constructor_NonStandardMode_DoesNotDisposeInitializationConnection()
    {
        var factory = new DisposalTrackingFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.SingleConnection
        };

        // Create DatabaseContext which should NOT dispose the initialization connection in non-Standard modes
        using var context = new DatabaseContext(config, factory);

        // Verify that the initialization connection was NOT disposed (it becomes the persistent connection)
        Assert.False(factory.InitializationConnection.WasDisposed, 
            "Initialization connection should not be disposed in non-Standard modes as it becomes the persistent connection");
        
        // Verify context was created successfully
        Assert.Equal(DbMode.SingleConnection, context.ConnectionMode);
        Assert.Equal(SupportedDatabase.Sqlite, context.Product);
    }

    private sealed class DisposalTrackingFactory : DbProviderFactory
    {
        public DisposalTrackingConnection InitializationConnection { get; }

        public DisposalTrackingFactory(SupportedDatabase product)
        {
            InitializationConnection = new DisposalTrackingConnection { EmulatedProduct = product };
        }

        public override DbConnection CreateConnection()
        {
            return InitializationConnection;
        }

        public override DbCommand CreateCommand()
        {
            return new fakeDbCommand();
        }

        public override DbParameter CreateParameter()
        {
            return new fakeDbParameter();
        }
    }

    private sealed class DisposalTrackingConnection : fakeDbConnection
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            WasDisposed = true;
            await base.DisposeAsync();
        }
    }
}
