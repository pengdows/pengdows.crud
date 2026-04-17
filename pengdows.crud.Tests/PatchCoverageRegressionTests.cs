using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using pengdows.crud.types;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PatchCoverageRegressionTests
{
    private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void CompleteTransactionWithWait_WhenCompletionLockWasDisposed_DoesNotThrowOnRelease()
    {
        using var context = CreateContext();
        var tx = CreateSyntheticTransactionContext(context);

        var method = typeof(TransactionContext).GetMethod("CompleteTransactionWithWait", AnyInstance);
        Assert.NotNull(method);

        method!.Invoke(tx, new object[]
        {
            new Action(() => DisposeCompletionLock(tx)),
            false
        });

        Assert.True(tx.IsCompleted);
        Assert.True(tx.WasRolledBack);
    }

    [Fact]
    public async Task CompleteTransactionWithWaitAsync_WhenCompletionLockWasDisposed_DoesNotThrowOnRelease()
    {
        using var context = CreateContext();
        var tx = CreateSyntheticTransactionContext(context);

        var method = typeof(TransactionContext).GetMethod("CompleteTransactionWithWaitAsync", AnyInstance);
        Assert.NotNull(method);

        var valueTask = Assert.IsType<ValueTask>(method!.Invoke(tx, new object?[]
        {
            new Func<ValueTask>(() =>
            {
                DisposeCompletionLock(tx);
                return ValueTask.CompletedTask;
            }),
            false,
            CancellationToken.None
        }));

        await valueTask;

        Assert.True(tx.IsCompleted);
        Assert.True(tx.WasRolledBack);
    }

    [Fact]
    public void DisposeManaged_WhenRollbackDisposesCompletionLock_DoesNotThrow()
    {
        using var context = CreateContext();
        var tx = CreateSyntheticTransactionContext(context);

        var callbackTransaction = (CallbackDbTransaction)GetField(tx, "_transaction")!;
        callbackTransaction.RollbackAction = () => DisposeCompletionLock(tx);

        var method = typeof(TransactionContext).GetMethod("DisposeManaged", AnyInstance);
        Assert.NotNull(method);
        method!.Invoke(tx, null);

        Assert.True(tx.IsCompleted);
        Assert.True(tx.WasRolledBack);
    }

    [Fact]
    public async Task DisposeManagedAsync_WhenRollbackDisposesCompletionLock_DoesNotThrow()
    {
        await using var context = CreateContext();
        var tx = CreateSyntheticTransactionContext(context);

        var callbackTransaction = (CallbackDbTransaction)GetField(tx, "_transaction")!;
        callbackTransaction.RollbackAction = () => DisposeCompletionLock(tx);

        var method = typeof(TransactionContext).GetMethod("DisposeManagedAsync", AnyInstance);
        Assert.NotNull(method);

        var valueTask = Assert.IsType<ValueTask>(method!.Invoke(tx, null));
        await valueTask;

        Assert.True(tx.IsCompleted);
        Assert.True(tx.WasRolledBack);
    }

    [Fact]
    public async Task DisposeManagedAsync_WhenRollbackThrows_LogsAndCompletes()
    {
        await using var context = CreateContext();
        var tx = CreateSyntheticTransactionContext(context);

        var callbackTransaction = (CallbackDbTransaction)GetField(tx, "_transaction")!;
        callbackTransaction.RollbackAction = () => throw new InvalidOperationException("rollback failed");

        var method = typeof(TransactionContext).GetMethod("DisposeManagedAsync", AnyInstance);
        Assert.NotNull(method);

        var valueTask = Assert.IsType<ValueTask>(method!.Invoke(tx, null));
        await valueTask;
    }

    [Fact]
    public async Task FirstOpenHandlerAsyncRw_WhenCanceled_PropagatesOperationCanceledException()
    {
        await using var context = CreateContext();
        ForceSessionSettings(context, "SET rw", "SET ro");

        using var tracked = new TrackedConnection(new AsyncBehaviorConnection());
        var handler = (Func<ITrackedConnection, CancellationToken, Task>)GetField(context, "_firstOpenHandlerAsyncRw")!;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler(tracked, cts.Token));
    }

    [Fact]
    public async Task FirstOpenHandlerAsyncRo_WhenCanceled_PropagatesOperationCanceledException()
    {
        await using var context = CreateContext();
        ForceSessionSettings(context, "SET rw", "SET ro");

        using var tracked = new TrackedConnection(new AsyncBehaviorConnection());
        var handler = (Func<ITrackedConnection, CancellationToken, Task>)GetField(context, "_firstOpenHandlerAsyncRo")!;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler(tracked, cts.Token));
    }

    [Fact]
    public async Task FirstOpenHandlerAsyncRw_WhenLoggingThrows_OuterCatchSwallowsFailure()
    {
        await using var context = CreateContext();
        ForceSessionSettings(context, "SET rw", "SET ro");
        SetField(context, "_logger", new ThrowOnceLogger<IDatabaseContext>());

        using var tracked = new TrackedConnection(new AsyncBehaviorConnection(new InvalidOperationException("boom")));
        var handler = (Func<ITrackedConnection, CancellationToken, Task>)GetField(context, "_firstOpenHandlerAsyncRw")!;

        await handler(tracked, CancellationToken.None);

        Assert.False(tracked.LocalState.SessionSettingsApplied);
    }

    [Fact]
    public async Task FirstOpenHandlerAsyncRo_WhenLoggingThrows_OuterCatchSwallowsFailure()
    {
        await using var context = CreateContext();
        ForceSessionSettings(context, "SET rw", "SET ro");
        SetField(context, "_logger", new ThrowOnceLogger<IDatabaseContext>());

        using var tracked = new TrackedConnection(new AsyncBehaviorConnection(new InvalidOperationException("boom")));
        var handler = (Func<ITrackedConnection, CancellationToken, Task>)GetField(context, "_firstOpenHandlerAsyncRo")!;

        await handler(tracked, CancellationToken.None);

        Assert.False(tracked.LocalState.SessionSettingsApplied);
    }

    [Fact]
    public void AdvancedTypeRegistry_SqliteDecimalMapping_HandlesDecimalAndConvertibleValues()
    {
        var registry = AdvancedTypeRegistry.Shared;
        var mapping = registry.GetMapping(typeof(decimal), SupportedDatabase.Sqlite);
        Assert.NotNull(mapping);

        Assert.NotNull(mapping!.ConfigureParameter);

        var decimalParameter = new TestDbParameter();
        mapping.ConfigureParameter!(decimalParameter, 12.34m);

        Assert.Equal(DbType.Double, decimalParameter.DbType);
        Assert.Equal(12.34d, decimalParameter.Value);
        Assert.True(decimalParameter.Precision >= 18);
        Assert.Equal(2, decimalParameter.Scale);

        var convertibleParameter = new TestDbParameter();
        Assert.NotNull(mapping.ConfigureParameter);
        mapping.ConfigureParameter!(convertibleParameter, "56.78");

        Assert.Equal(DbType.Double, convertibleParameter.DbType);
        Assert.Equal(56.78d, convertibleParameter.Value);
        Assert.True(convertibleParameter.Precision >= 18);
        Assert.Equal(2, convertibleParameter.Scale);
    }

    private static DatabaseContext CreateContext()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=patch-coverage;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection
        };

        return new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite), NullLoggerFactory.Instance);
    }

    private static TransactionContext CreateSyntheticTransactionContext(DatabaseContext context)
    {
        var ctor = typeof(TransactionContext).GetConstructor(
            AnyInstance,
            null,
            new[]
            {
                typeof(IDatabaseContext),
                typeof(ITrackedConnection),
                typeof(IDbTransaction),
                typeof(IsolationLevel),
                typeof(ExecutionType),
                typeof(ILogger<TransactionContext>)
            },
            null);
        Assert.NotNull(ctor);

        var connection = context.GetConnection(ExecutionType.Write, false);
        var transaction = new CallbackDbTransaction();

        return (TransactionContext)ctor!.Invoke(new object?[]
        {
            context,
            connection,
            transaction,
            IsolationLevel.ReadCommitted,
            ExecutionType.Write,
            NullLogger<TransactionContext>.Instance
        });
    }

    private static void ForceSessionSettings(DatabaseContext context, string readWriteSql, string readOnlySql)
    {
        SetField(context, "_sessionSettingsDetectionCompleted", true);
        SetField(context, "_cachedReadWriteSessionSettings", readWriteSql);
        SetField(context, "_cachedReadOnlySessionSettings", readOnlySql);
    }

    private static void DisposeCompletionLock(TransactionContext tx)
    {
        var completionLock = (SemaphoreSlim)GetField(tx, "_completionLock")!;
        completionLock.Dispose();
    }

    private static object? GetField(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, AnyInstance);
        Assert.NotNull(field);
        return field!.GetValue(target);
    }

    private static void SetField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, AnyInstance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private sealed class CallbackDbTransaction : IDbTransaction
    {
        public Action? RollbackAction { get; set; }

        public IDbConnection? Connection => null;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        public void Commit()
        {
        }

        public void Dispose()
        {
        }

        public void Rollback()
        {
            RollbackAction?.Invoke();
        }
    }

    private sealed class AsyncBehaviorConnection : DbConnection
    {
        private readonly Exception? _exception;
        private string _connectionString = string.Empty;

        public AsyncBehaviorConnection(Exception? exception = null)
        {
            _exception = exception;
        }

        [AllowNull]
        public override string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public override int ConnectionTimeout => 30;
        public override string Database => "patch";
        public override string DataSource => "patch";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotSupportedException();
        }

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbCommand CreateDbCommand()
        {
            return new AsyncBehaviorCommand(_exception);
        }
    }

    private sealed class AsyncBehaviorCommand : DbCommand
    {
        private readonly Exception? _exception;
        private string _commandText = string.Empty;

        public AsyncBehaviorCommand(Exception? exception)
        {
            _exception = exception;
        }

        [AllowNull]
        public override string CommandText
        {
            get => _commandText;
            set => _commandText = value ?? string.Empty;
        }

        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; } = CommandType.Text;
        public override bool DesignTimeVisible { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
        protected override DbTransaction? DbTransaction { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        public override void Cancel()
        {
        }

        protected override DbParameter CreateDbParameter()
        {
            throw new NotSupportedException();
        }

        public override int ExecuteNonQuery()
        {
            throw new NotSupportedException();
        }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_exception != null)
            {
                return Task.FromException<int>(_exception);
            }

            return Task.FromResult(0);
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            throw new NotSupportedException();
        }

        public override object? ExecuteScalar()
        {
            throw new NotSupportedException();
        }

        public override void Prepare()
        {
        }
    }

    private sealed class ThrowOnceLogger<T> : ILogger<T>
    {
        private int _errorCount;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NoopDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error && Interlocked.Increment(ref _errorCount) == 1)
            {
                throw new InvalidOperationException("logger failure");
            }
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class TestDbParameter : DbParameter
    {
        private string _parameterName = string.Empty;
        private string _sourceColumn = string.Empty;

        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName
        {
            get => _parameterName;
            set => _parameterName = value ?? string.Empty;
        }

        public override int Size { get; set; }
        public override byte Precision { get; set; }
        public override byte Scale { get; set; }

        [AllowNull]
        public override string SourceColumn
        {
            get => _sourceColumn;
            set => _sourceColumn = value ?? string.Empty;
        }

        public override bool SourceColumnNullMapping { get; set; }
        [AllowNull] public override object Value { get; set; } = DBNull.Value;

        public override void ResetDbType()
        {
            DbType = DbType.Object;
        }
    }
}
