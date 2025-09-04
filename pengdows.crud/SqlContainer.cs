#region

using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.collections;
using pengdows.crud.connection;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using pengdows.crud.strategies.proc;

#endregion

namespace pengdows.crud;

public class SqlContainer : SafeAsyncDisposableBase, ISqlContainer
{
    private readonly IDatabaseContext _context;
    private readonly ISqlDialect _dialect;

    private readonly ILogger<ISqlContainer> _logger;
    private readonly IDictionary<string, DbParameter> _parameters = new OrderedDictionary<string, DbParameter>();
    private int _outputParameterCount;

    internal SqlContainer(IDatabaseContext context, string? query = "", ILogger<ISqlContainer>? logger = null)
    {
        _context = context;
        _dialect = (context as ISqlDialectProvider)?.Dialect
                   ?? throw new InvalidOperationException(
                       "IDatabaseContext must implement ISqlDialectProvider and expose a non-null Dialect.");
        _logger = logger ?? NullLogger<ISqlContainer>.Instance;
        Query = StringBuilderPool.Get(query);
    }

    public StringBuilder Query { get; }

    public int ParameterCount => _parameters.Count;

    public bool HasWhereAppended { get; set; }

    public string QuotePrefix => _dialect.QuotePrefix;

    public string QuoteSuffix => _dialect.QuoteSuffix;

    public string CompositeIdentifierSeparator => _dialect.CompositeIdentifierSeparator;

    public string WrapObjectName(string name)
    {
        return _dialect.WrapObjectName(name);
    }

    public string MakeParameterName(DbParameter dbParameter)
    {
        return _dialect.MakeParameterName(dbParameter);
    }

    public string MakeParameterName(string parameterName)
    {
        return _dialect.MakeParameterName(parameterName);
    }

    

    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        return _dialect.CreateDbParameter(name, type, value);
    }

    public DbParameter CreateDbParameter<T>(DbType type, T value)
    {
        return _dialect.CreateDbParameter(type, value);
    }


    public void AddParameter(DbParameter parameter)
    {
        if (parameter == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(parameter.ParameterName))
        {
            parameter.ParameterName = GenerateRandomName();
        }

        var isOutput = parameter.Direction switch
        {
            ParameterDirection.Output => true,
            ParameterDirection.InputOutput => true,
            ParameterDirection.ReturnValue => true,
            _ => false
        };

        if (isOutput)
        {
            var next = _outputParameterCount + 1;
            var maxOut = _context.DataSourceInfo.MaxOutputParameters;
            if (next > maxOut)
            {
                throw new InvalidOperationException(
                    $"Query exceeds the maximum output parameter limit of {maxOut} for {_context.DatabaseProductName}.");
            }

            _outputParameterCount = next;
        }

        _parameters.Add(parameter.ParameterName, parameter);
    }

    public DbParameter AddParameterWithValue<T>(DbType type, T value,
        ParameterDirection direction = ParameterDirection.Input)
    {
        if (value is DbParameter)
        {
            throw new ArgumentException("Parameter type can't be DbParameter.");
        }

        return AddParameterWithValue(null, type, value, direction);
    }

    public DbParameter AddParameterWithValue<T>(string? name, DbType type, T value,
        ParameterDirection direction = ParameterDirection.Input)
    {
        name ??= GenerateRandomName();
        var parameter = _context.CreateDbParameter(name, type, value);
        parameter.Direction = direction;

        AddParameter(parameter);
        return parameter;
    }

    // Back-compat overloads (interface surface)
    public DbParameter AddParameterWithValue<T>(DbType type, T value)
    {
        return AddParameterWithValue(type, value, ParameterDirection.Input);
    }

    public DbParameter AddParameterWithValue<T>(string? name, DbType type, T value)
    {
        return AddParameterWithValue(name, type, value, ParameterDirection.Input);
    }

    private string NormalizeParameterName(string parameterName)
    {
        // Normalize so lookups work with or without a leading marker
        // (e.g., @p0, :p0, ?p0, $p0 -> p0) for named providers.
        return _dialect.SupportsNamedParameters
            ? parameterName.TrimStart('@', ':', '?', '$')
            : parameterName;
    }

    public void SetParameterValue(string parameterName, object? newValue)
    {
        var normalizedName = NormalizeParameterName(parameterName);
        if (!_parameters.TryGetValue(normalizedName, out var parameter))
        {
            throw new KeyNotFoundException($"Parameter '{parameterName}' not found.");
        }

        parameter.Value = newValue;
    }

    public object? GetParameterValue(string parameterName)
    {
        var normalizedName = NormalizeParameterName(parameterName);
        if (!_parameters.TryGetValue(normalizedName, out var parameter))
        {
            throw new KeyNotFoundException($"Parameter '{parameterName}' not found.");
        }

        return parameter.Value;
    }

    public T GetParameterValue<T>(string parameterName)
    {
        var value = GetParameterValue(parameterName);
        var sourceType = value?.GetType() ?? typeof(object);
        var coerced = TypeCoercionHelper.Coerce(value, sourceType, typeof(T));

        return (T)coerced!;
    }



    public DbCommand CreateCommand(ITrackedConnection conn)
    {
        var cmd = conn.CreateCommand();
        if (_context is TransactionContext transactionContext)
        {
            cmd.Transaction = (transactionContext.Transaction as DbTransaction)
                              ?? throw new InvalidOperationException("Transaction is not a transaction");
        }

        return (cmd as DbCommand)
               ?? throw new InvalidOperationException("Command is not a DbCommand");
    }

    public void Clear()
    {
        Query.Clear();
        _parameters.Clear();
        _outputParameterCount = 0;
    }

    public string WrapForStoredProc(ExecutionType executionType, bool includeParameters = true, bool captureReturn = false)
    {
        var procName = Query.ToString().Trim();

        if (string.IsNullOrWhiteSpace(procName))
        {
            throw new InvalidOperationException("Procedure name is missing from the query.");
        }

        if (_context.ProcWrappingStyle == ProcWrappingStyle.None)
        {
            throw new NotSupportedException(
                $"Stored procedures are not supported for {_context.Product}.");
        }

        var args = includeParameters ? BuildProcedureArguments() : string.Empty;

        if (captureReturn)
        {
            // Only Exec style (e.g., SQL Server) supports capturing a return value in our abstraction
            if (_context.ProcWrappingStyle != ProcWrappingStyle.Exec)
            {
                throw new NotSupportedException("Capturing return value is not supported for this provider.");
            }

            return FormatExecWithReturn();
        }

        var strategy = ProcWrappingStrategyFactory.Create(_context.ProcWrappingStyle);
        return strategy.Wrap(procName, executionType, args, WrapObjectName);

        string FormatExecWithReturn()
        {
            var paramList = string.IsNullOrWhiteSpace(args) ? string.Empty : $" {args}";
            var wrappedProcName = WrapObjectName(procName);
            return $"DECLARE @__ret INT;\nEXEC @__ret = {wrappedProcName}{paramList};\nSELECT @__ret;";
        }

        string BuildProcedureArguments()
        {
            if (_parameters.Count == 0)
            {
                return string.Empty;
            }

            if (_context.SupportsNamedParameters)
            {
                // Trust that dev has set correct names
                return string.Join(", ", _parameters.Values.Select(p => _context.MakeParameterName(p)));
            }

            return string.Join(", ", Enumerable.Repeat("?", _parameters.Count));
        }
    }

    // Overload without defaults to avoid ambiguity with the 3-arg version
    public string WrapForStoredProc(ExecutionType executionType, bool includeParameters)
    {
        return WrapForStoredProc(executionType, includeParameters, captureReturn: false);
    }

    public string WrapForCreateWithReturn(bool includeParameters = true)
    {
        return WrapForStoredProc(ExecutionType.Write, includeParameters, captureReturn: true);
    }

    public string WrapForUpdateWithReturn(bool includeParameters = true)
    {
        return WrapForStoredProc(ExecutionType.Write, includeParameters, captureReturn: true);
    }

    public string WrapForDeleteWithReturn(bool includeParameters = true)
    {
        return WrapForStoredProc(ExecutionType.Write, includeParameters, captureReturn: true);
    }
    private string GenerateRandomName()
    {
        const int maxAttempts = 1000;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var name = _context.GenerateRandomName();
            if (!_parameters.ContainsKey(name))
            {
                return name;
            }
        }
        
        // Fallback: use timestamp-based name to guarantee uniqueness
        return $"p_{DateTimeOffset.UtcNow.Ticks}_{Guid.NewGuid():N}".Substring(0, 30);
    }

    public async Task<int> ExecuteNonQueryAsync(CommandType commandType = CommandType.Text)
    {
        return await ExecuteNonQueryAsync(commandType, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<int> ExecuteNonQueryAsync(CommandType commandType, CancellationToken cancellationToken)
    {
        // Check if context is configured as read-only (exactly ReadWriteMode.ReadOnly, not ReadWrite)
        if (_context is DatabaseContext dbContext && 
            dbContext.ReadWriteMode == ReadWriteMode.ReadOnly)
        {
            throw new NotSupportedException("Write operations are not supported in read-only mode.");
        }
        
        _context.AssertIsWriteConnection();
        ITrackedConnection? conn = null;
        DbCommand? cmd = null;
        try
        {
            await using var contextLocker = _context.GetLock();
            await contextLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            var isTransaction = _context is ITransactionContext;
            conn = _context.GetConnection(ExecutionType.Write, isTransaction);
            if (_context.ConnectionMode == DbMode.SingleWriter &&
                _context is DatabaseContext dbCtx &&
                !ReferenceEquals(conn, dbCtx.PersistentConnection))
            {
                throw new InvalidOperationException("SingleWriter: writes must use the shared writer connection.");
            }
            await using var connectionLocker = conn.GetLock();
            await connectionLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            cmd = await PrepareAndCreateCommandAsync(conn, commandType, ExecutionType.Write, cancellationToken).ConfigureAwait(false);

            return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Cleanup(cmd, conn, ExecutionType.Write);
        }
    }

    public async Task<T?> ExecuteScalarAsync<T>(CommandType commandType = CommandType.Text)
    {
        return await ExecuteScalarAsync<T>(commandType, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<T?> ExecuteScalarAsync<T>(CommandType commandType, CancellationToken cancellationToken)
    {
        _context.AssertIsReadConnection();

        await using var reader = await ExecuteReaderAsync(commandType, cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            var value = reader.GetValue(0); // always returns object
            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T?)TypeCoercionHelper.Coerce(value, reader.GetFieldType(0), targetType);
        }

        // Return default for nullable types, throw for non-nullable types (following ADO.NET ExecuteScalar behavior)
        var isNullable = !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;
        if (isNullable)
        {
            return default(T);
        }

        throw new InvalidOperationException("ExecuteScalarAsync expected at least one row but found none.");
    }

    // Write-path scalar execution (e.g., INSERT ... RETURNING / OUTPUT)
    public async Task<T?> ExecuteScalarWriteAsync<T>(CommandType commandType = CommandType.Text)
    {
        return await ExecuteScalarWriteAsync<T>(commandType, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<T?> ExecuteScalarWriteAsync<T>(CommandType commandType, CancellationToken cancellationToken)
    {
        // Check for explicit read-only mode
        if (_context is DatabaseContext dbContext && dbContext.ReadWriteMode == ReadWriteMode.ReadOnly)
        {
            throw new NotSupportedException("Write operations are not supported in read-only mode.");
        }

        _context.AssertIsWriteConnection();
        ITrackedConnection? conn = null;
        DbCommand? cmd = null;
        try
        {
            await using var contextLocker = _context.GetLock();
            await contextLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            var isTransaction = _context is ITransactionContext;
            conn = _context.GetConnection(ExecutionType.Write, isTransaction);
            if (_context.ConnectionMode == DbMode.SingleWriter &&
                _context is DatabaseContext dbCtx &&
                !ReferenceEquals(conn, dbCtx.PersistentConnection))
            {
                throw new InvalidOperationException("SingleWriter: writes must use the shared writer connection.");
            }
            await using var connectionLocker = conn.GetLock();
            await connectionLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            cmd = await PrepareAndCreateCommandAsync(conn, commandType, ExecutionType.Write, cancellationToken).ConfigureAwait(false);

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is null || result is DBNull)
            {
                var isNullable = !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;
                return isNullable ? default : throw new InvalidOperationException("ExecuteScalarWriteAsync expected a value but found none.");
            }

            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T?)TypeCoercionHelper.Coerce(result, result.GetType(), targetType);
        }
        finally
        {
            Cleanup(cmd, conn, ExecutionType.Write);
        }
    }

    public async Task<ITrackedReader> ExecuteReaderAsync(CommandType commandType = CommandType.Text)
    {
        return await ExecuteReaderAsync(commandType, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<ITrackedReader> ExecuteReaderAsync(CommandType commandType, CancellationToken cancellationToken)
    {
        _context.AssertIsReadConnection();

        ITrackedConnection conn;
        DbCommand? cmd = null;
        try
        {
            await using var contextLocker = _context.GetLock();
            await contextLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            var isTransaction = _context is ITransactionContext;
            conn = _context.GetConnection(ExecutionType.Read, isTransaction);
            var connectionLocker = conn.GetLock();
            await connectionLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            cmd = await PrepareAndCreateCommandAsync(conn, commandType, ExecutionType.Read, cancellationToken).ConfigureAwait(false);

            // unless the databaseContext is in a transaction or SingleConnection mode,
            // a new connection is returned for every READ operation, therefore, we
            // are going to set the connection to close and dispose when the reader is
            // closed. This prevents leaking
            var isSingleConnection = _context.ConnectionMode == DbMode.SingleConnection;
            var isReadOnlyConnection = _context.IsReadOnlyConnection;
            var behavior = (isTransaction || isSingleConnection)
                ? CommandBehavior.Default
                : CommandBehavior.CloseConnection;
            //behavior |= CommandBehavior.SingleResult;

            // if this is our single connection to the database, for a transaction
            //or sqlCe mode, or single connection mode, we will NOT close the connection.
            // otherwise, we will have the connection set to autoclose so that we
            //close the underlying connection when the DbDataReader is closed;
            var dr = await cmd.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
            return new TrackedReader(dr, conn, connectionLocker, behavior == CommandBehavior.CloseConnection);
        }
        finally
        {
            //no matter what we do NOT close the underlying connection
            //or dispose it.
            Cleanup(cmd, null, ExecutionType.Read);
        }
    }

    // Optimized single-row reader to hint providers/ADO.NET for minimal result shape
    public async Task<ITrackedReader> ExecuteReaderSingleRowAsync(CancellationToken cancellationToken = default)
    {
        _context.AssertIsReadConnection();

        ITrackedConnection conn;
        DbCommand? cmd = null;
        try
        {
            await using var contextLocker = _context.GetLock();
            await contextLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            var isTransaction = _context is ITransactionContext;
            conn = _context.GetConnection(ExecutionType.Read, isTransaction);
            var connectionLocker = conn.GetLock();
            await connectionLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            cmd = await PrepareAndCreateCommandAsync(conn, CommandType.Text, ExecutionType.Read, cancellationToken).ConfigureAwait(false);

            var isSingleConnection = _context.ConnectionMode == DbMode.SingleConnection;
            var behavior = (isTransaction || isSingleConnection)
                ? CommandBehavior.SingleRow
                : (CommandBehavior.CloseConnection | CommandBehavior.SingleRow);

            var dr = await cmd.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
            return new TrackedReader(dr, conn, connectionLocker, (behavior & CommandBehavior.CloseConnection) == CommandBehavior.CloseConnection);
        }
        finally
        {
            Cleanup(cmd, null, ExecutionType.Read);
        }
    }

    public void AddParameters(IEnumerable<DbParameter> list)
    {
        if (list != null)
        {
            foreach (var p in list.OfType<DbParameter>())
            {
                AddParameter(p);
            }
        }
    }


    private async Task<DbCommand> PrepareAndCreateCommandAsync(
        ITrackedConnection conn,
        CommandType commandType,
        ExecutionType executionType,
        CancellationToken cancellationToken)
    {
        if (commandType == CommandType.TableDirect)
        {
            throw new NotSupportedException("TableDirect isn't supported.");
        }

        if (Query.Length == 0)
        {
            throw new InvalidOperationException("SQL query is empty.");
        }

        await OpenConnectionAsync(conn, cancellationToken).ConfigureAwait(false);
        var cmd = CreateCommand(conn);
        cmd.CommandType = CommandType.Text;
        // Compute command text once to avoid double ToString() and guard logging
        var cmdText = (commandType == CommandType.StoredProcedure)
            ? WrapForStoredProc(executionType, includeParameters: true)
            : Query.ToString();
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Executing SQL: {Sql}", cmdText);
        }
        if (_parameters.Count > 0 && _logger.IsEnabled(LogLevel.Debug))
        {
            var paramDump = string.Join(", ",
                _parameters.Values.Select(p => $"{p.ParameterName}={p.Value ?? "NULL"}"));
            _logger.LogDebug("Parameters: {Parameters}", paramDump);
        }
        cmd.CommandText = cmdText;
        if (_parameters.Count > _context.MaxParameterLimit)
        {
            throw new InvalidOperationException(
                $"Query exceeds the maximum parameter limit of {_context.MaxParameterLimit} for {_context.DatabaseProductName}.");
        }

        foreach (var param in _parameters.Values)
        {
            cmd.Parameters.Add(param);
        }

        // Apply shape-aware prepare logic
        MaybePrepareCommand(cmd, conn);

        return cmd;
    }

    /// <summary>
    /// Applies shape-aware prepare logic: prepares once per connection per SQL shape,
    /// with fallback to disable prepare on provider failures.
    /// </summary>
    private void MaybePrepareCommand(DbCommand cmd, ITrackedConnection conn)
    {
        // Determine effective prepare setting based on configuration overrides
        var shouldPrepare = ComputeEffectivePrepareSettings();
        
        // Check if prepare is disabled globally or for this connection
        if (!shouldPrepare || conn.LocalState.PrepareDisabled)
        {
            return;
        }

        // Compute command shape hash for caching
        var shapeHash = ConnectionLocalState.ComputeShapeHash(cmd);
        
        // Skip if already prepared for this shape
        if (conn.LocalState.IsAlreadyPreparedForShape(shapeHash))
        {
            return;
        }

        try
        {
            cmd.Prepare();
            conn.LocalState.MarkShapePrepared(shapeHash);
        }
        catch (Exception ex)
        {
            // Check if this type of exception should disable prepare for this connection
            if (_dialect.ShouldDisablePrepareOn(ex))
            {
                conn.LocalState.PrepareDisabled = true;
                // Log at debug level to avoid noise - this is expected fallback behavior
                _logger?.LogDebug(ex, 
                    "Disabled prepare for connection due to provider exception: {ExceptionType}", 
                    ex.GetType().Name);
            }
            else
            {
                // Re-throw unexpected exceptions
                throw;
            }
        }
    }

    /// <summary>
    /// Computes the effective prepare setting based on configuration overrides and dialect defaults
    /// </summary>
    private bool ComputeEffectivePrepareSettings()
    {
        // Check if prepare is hard-disabled via configuration
        if (_context.DisablePrepare == true)
        {
            return false;
        }
        
        // Check if prepare is explicitly forced on or off via configuration
        if (_context.ForceManualPrepare.HasValue)
        {
            return _context.ForceManualPrepare.Value;
        }
        
        // Fall back to dialect default
        return _dialect.PrepareStatements;
    }

    // Backward-compatible helper for tests using reflection to invoke a simplified prepare
    // signature. Intentionally minimal: the public execution paths perform full preparation.
    private Task PrepareCommandAsync(DbCommand _)
    {
        return Task.CompletedTask;
    }

    private Task OpenConnectionAsync(ITrackedConnection conn, CancellationToken cancellationToken)
    {
        if (conn.State != ConnectionState.Open)
        {
            return conn.OpenAsync(cancellationToken);
        }

        return Task.CompletedTask;
    }

    private void Cleanup(DbCommand? cmd, ITrackedConnection? conn, ExecutionType executionType)
    {
        if (cmd != null)
        {
            try
            {
                cmd.Parameters?.Clear();
                cmd.Connection = null;
                cmd.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Command disposal failed: {ex.Message}");
                // We're intentionally not retrying here anymore — disposal failure is generally harmless in this case
            }
        }

        // Don't dispose read connections — they are left open until the reader disposes
        if (executionType == ExecutionType.Read)
        {
            return;
        }

        if (_context is not TransactionContext && conn is not null)
        {
            _context.CloseAndDisposeConnection(conn);
        }
    }

    protected override void DisposeManaged()
    {
        // Dispose managed resources here (clear parameters and return the builder to pool)
        _parameters.Clear();
        _outputParameterCount = 0;
        StringBuilderPool.Return(Query);
    }
}
