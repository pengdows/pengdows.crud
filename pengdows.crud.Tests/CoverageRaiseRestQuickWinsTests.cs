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
    /// <summary>
    /// BP-209: on PostgreSQL SafeNonBlockingReads runs as RepeatableRead (MVCC snapshot) on the
    /// async path too, instead of throwing.
    /// </summary>
    [Fact]
    public async Task BeginTransactionAsync_SafeNonBlockingReadsOnPostgreSql_UsesRepeatableRead()
    {
        await using var context = new DatabaseContext(
            "Host=localhost;Database=test",
            new fakeDbFactory(SupportedDatabase.PostgreSql));

        await using var tx = await context.BeginTransactionAsync(IsolationProfile.SafeNonBlockingReads);
        Assert.Equal(System.Data.IsolationLevel.RepeatableRead, tx.IsolationLevel);
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
        public IDbCommand CreateCommand() => new ScalarCommand(null, throwOnExecute: true);
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
        private readonly bool _throwOnExecute;
        public ScalarCommand(object? result, bool throwOnExecute = false)
        {
            _result = result;
            _throwOnExecute = throwOnExecute;
        }

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
        public override object? ExecuteScalar() =>
            _throwOnExecute ? throw new InvalidOperationException("Unsupported command.") : _result;
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new fakeDbParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) =>
            _throwOnExecute ? Task.FromException<object?>(new InvalidOperationException("Unsupported command.")) : Task.FromResult(_result);
    }
}
