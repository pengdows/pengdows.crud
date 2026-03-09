using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public class CoverageRaiseRestQuickWinsTests
{
    [Fact]
    public async Task BeginTransactionAsync_SafeNonBlockingReadsOnPostgreSql_Throws()
    {
        await using var context = new DatabaseContext(
            "Host=localhost;Database=test",
            new fakeDbFactory(SupportedDatabase.PostgreSql));

        await Assert.ThrowsAsync<TransactionModeNotSupportedException>(
            () => context.BeginTransactionAsync(IsolationProfile.SafeNonBlockingReads));
    }

    [Fact]
    public void BeginTransaction_ReadOnlyRequested_WhenContextNotReadable_Throws()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            ReadWriteMode = (ReadWriteMode)0
        };

        using var context = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite));
        Assert.Throws<InvalidOperationException>(() => context.BeginTransaction(readOnly: true));
    }

    [Fact]
    public void BeginTransaction_WriteModeWithReadExecutionType_Throws()
    {
        using var context = new DatabaseContext(
            "Data Source=:memory:",
            new fakeDbFactory(SupportedDatabase.Sqlite));

        Assert.Throws<InvalidOperationException>(() =>
            context.BeginTransaction(executionType: ExecutionType.Read, readOnly: false));
    }

    [Fact]
    public void BeginTransaction_WhenSupportedLevelsLackReadCommittedAndSerializable_UsesFirstSupportedLevel()
    {
        using var context = new DatabaseContext(
            "Data Source=:memory:",
            new fakeDbFactory(SupportedDatabase.Sqlite));

        var resolverField = typeof(DatabaseContext).GetField("_isolationResolver", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(resolverField);
        var resolver = resolverField!.GetValue(context);
        Assert.NotNull(resolver);

        var supportedField = resolver!.GetType().GetField("_supportedLevels", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(supportedField);
        supportedField!.SetValue(resolver, new HashSet<IsolationLevel> { IsolationLevel.Chaos });

        using var tx = context.BeginTransaction();
        Assert.NotNull(tx);
    }

    [Fact]
    public void DatabaseDetection_DetectFromConnection_NonDbConnectionPath_ReturnsUnknown()
    {
        using var conn = new PlainConnection();
        var detected = DatabaseDetectionService.DetectFromConnection(conn);
        Assert.Equal(SupportedDatabase.Unknown, detected);
    }

    [Fact]
    public void DatabaseDetection_DetectFromConnection_MySqlWithTiDbVersion_ReturnsTiDb()
    {
        using var conn = new SchemaConnection("MySQL", "TiDB v7.1");
        var detected = DatabaseDetectionService.DetectFromConnection(conn);
        Assert.Equal(SupportedDatabase.TiDb, detected);
    }

    [Fact]
    public void DatabaseDetection_DetectFromFactory_WhenFakePretendToBeGetterThrows_ReturnsUnknown()
    {
        var detected = DatabaseDetectionService.DetectFromFactory(new fakeThrowFactory());
        Assert.Equal(SupportedDatabase.Unknown, detected);
    }

    [Fact]
    public void DatabaseDetection_DetectTopology_FirebirdParseFailure_DoesNotThrow()
    {
        var topology = DatabaseDetectionService.DetectTopology(SupportedDatabase.Firebird, "\0");
        Assert.False(topology.IsEmbedded);
    }

    [Fact]
    public void YugabyteDialect_PrepareConnectionStringForDataSource_OnInvalidConnectionString_ReturnsOriginal()
    {
        var factory = new fakeDbFactory(SupportedDatabase.YugabyteDb)
        {
            ConnectionStringBuilderBehavior = ConnectionStringBuilderBehavior.ThrowOnConnectionStringSet
        };
        var dialect = new YugabyteDbDialect(factory, NullLogger.Instance);
        var cs = "Host=localhost;Database=test";

        var result = InvokePrepareConnectionStringForDataSource(dialect, cs);

        Assert.Equal(cs, result);
    }

    [Fact]
    public async Task DuckDbDialect_AdditionalUncoveredBranches_AreExercised()
    {
        var dialect = new DuckDbDialect(new fakeDbFactory(SupportedDatabase.DuckDB), NullLogger.Instance);

        Assert.False(dialect.SupportsMergeReturning);
        Assert.False(dialect.MergeUpdateRequiresTargetAlias);
        Assert.False(dialect.SupportsEncryption);
        Assert.False(dialect.SupportsFillWindowFunction);
        Assert.Equal("SELECT lastval()", dialect.GetLastInsertedIdQuery());
        Assert.Equal(" ", dialect.GetReadOnlyConnectionString(" "));

        Assert.NotNull(dialect.ParseVersion("DuckDB v1.4.2"));

        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var conn = new fakeDbConnection();
        conn.SetCommandFailure("SELECT version()", new InvalidOperationException("select failed"));
        conn.SetScalarResultForCommand("PRAGMA version", "DuckDB v1.4.2");
        factory.Connections.Add(conn);
        await using var ctx = new DatabaseContext("Data Source=:memory:", factory);
        var tracked = ctx.GetConnection(ExecutionType.Read, false);
        try
        {
            var name = await dialect.GetProductNameAsync(tracked);
            Assert.Equal("DuckDB", name);
        }
        finally
        {
            tracked.Dispose();
        }

        var factory2 = new fakeDbFactory(SupportedDatabase.DuckDB);
        var conn2 = new fakeDbConnection();
        conn2.ScalarResultsByCommand["SELECT version()"] = null!;
        factory2.Connections.Add(conn2);
        await using var ctx2 = new DatabaseContext("Data Source=:memory:", factory2);
        var tracked2 = ctx2.GetConnection(ExecutionType.Read, false);
        try
        {
            var version = await dialect.GetDatabaseVersionAsync(tracked2);
            Assert.Equal(string.Empty, version);
        }
        finally
        {
            tracked2.Dispose();
        }

        var factory3 = new fakeDbFactory(SupportedDatabase.DuckDB);
        var conn3 = new fakeDbConnection();
        conn3.SetCommandFailure("SELECT version()", new InvalidOperationException("select failed"));
        conn3.ScalarResultsByCommand["PRAGMA version"] = null!;
        factory3.Connections.Add(conn3);
        await using var ctx3 = new DatabaseContext("Data Source=:memory:", factory3);
        var tracked3 = ctx3.GetConnection(ExecutionType.Read, false);
        try
        {
            var version = await dialect.GetDatabaseVersionAsync(tracked3);
            Assert.Equal(string.Empty, version);
        }
        finally
        {
            tracked3.Dispose();
        }
    }

    [Fact]
    public void TableGatewaySql_CreateTemplateRowId_Branches_AreCovered()
    {
        var stringId = InvokeCreateTemplateRowId<string>();
        Assert.Equal(string.Empty, stringId);
        var intId = InvokeCreateTemplateRowId<int>();
        Assert.Equal(0, intId);
    }

    [Fact]
    public void TableGatewaySql_UnsupportedRowIdType_FailsAtTypeInitialization()
    {
        var ex = Assert.Throws<TypeInitializationException>(() =>
            RuntimeHelpers.RunClassConstructor(typeof(TableGateway<DummyEntity, PrivateCtorRowId>).TypeHandle));
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void TableGatewaySql_CreateTemplateRowIds_CountMustBePositive()
    {
        var closed = typeof(TableGateway<,>).MakeGenericType(typeof(DummyEntity), typeof(Guid));
        var method = closed.GetMethod("CreateTemplateRowIds", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var ex = Assert.Throws<TargetInvocationException>(() => method!.Invoke(null, new object[] { 0 }));
        Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
    }

    private static string InvokePrepareConnectionStringForDataSource(YugabyteDbDialect dialect, string value)
    {
        var method = typeof(YugabyteDbDialect).GetMethod(
            "PrepareConnectionStringForDataSource",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return (string)method!.Invoke(dialect, new object[] { value, false })!;
    }

    private static T InvokeCreateTemplateRowId<T>()
    {
        var method = GetCreateTemplateRowIdMethod(typeof(T));
        return (T)method.Invoke(null, null)!;
    }

    private static MethodInfo GetCreateTemplateRowIdMethod(Type rowIdType)
    {
        var closed = typeof(TableGateway<,>).MakeGenericType(typeof(DummyEntity), rowIdType);
        var method = closed.GetMethod("CreateTemplateRowId", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method!;
    }

    private sealed class fakeThrowFactory : DbProviderFactory
    {
        public SupportedDatabase PretendToBe => throw new InvalidOperationException("boom");
    }

    [Table("dummy_entity")]
    private sealed class DummyEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }
    }

    private sealed class PrivateCtorRowId
    {
        private PrivateCtorRowId() { }
    }

    private sealed class PlainConnection : IDbConnection
    {
        [AllowNull]
        public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "db";
        public ConnectionState State => ConnectionState.Open;
        public IDbTransaction BeginTransaction() => throw new NotSupportedException();
        public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotSupportedException();
        public void ChangeDatabase(string databaseName) { }
        public void Close() { }
        public IDbCommand CreateCommand() => new ScalarCommand(string.Empty);
        public void Open() { }
        public void Dispose() { }
    }

    private sealed class SchemaConnection : DbConnection
    {
        private readonly string _productName;
        private readonly string _productVersion;

        public SchemaConnection(string productName, string productVersion)
        {
            _productName = productName;
            _productVersion = productVersion;
        }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "db";
        public override string DataSource => "ds";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;
        public override int ConnectionTimeout => 0;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new ScalarCommand(string.Empty);

        public override DataTable GetSchema(string collectionName)
        {
            if (collectionName != DbMetaDataCollectionNames.DataSourceInformation)
            {
                return new DataTable();
            }

            var table = new DataTable();
            table.Columns.Add("DataSourceProductName", typeof(string));
            table.Columns.Add("DataSourceProductVersion", typeof(string));
            var row = table.NewRow();
            row["DataSourceProductName"] = _productName;
            row["DataSourceProductVersion"] = _productVersion;
            table.Rows.Add(row);
            return table;
        }
    }

    private sealed class ScalarCommand : DbCommand
    {
        private sealed class EmptyParameterCollection : DbParameterCollection
        {
            public override int Count => 0;
            public override object SyncRoot => this;
            public override int Add(object value) => 0;
            public override void AddRange(Array values) { }
            public override void Clear() { }
            public override bool Contains(object value) => false;
            public override bool Contains(string value) => false;
            public override void CopyTo(Array array, int index) { }
            public override System.Collections.IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
            public override int IndexOf(object value) => -1;
            public override int IndexOf(string parameterName) => -1;
            public override void Insert(int index, object value) { }
            public override void Remove(object value) { }
            public override void RemoveAt(int index) { }
            public override void RemoveAt(string parameterName) { }
            protected override DbParameter GetParameter(int index) => throw new IndexOutOfRangeException();
            protected override DbParameter GetParameter(string parameterName) => throw new IndexOutOfRangeException();
            protected override void SetParameter(int index, DbParameter value) { }
            protected override void SetParameter(string parameterName, DbParameter value) { }
        }

        private readonly DbParameterCollection _parameters = new EmptyParameterCollection();
        private readonly object? _result;
        public ScalarCommand(object? result) => _result = result;

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => _result;
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new fakeDbParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) => Task.FromResult(_result);
    }
}
