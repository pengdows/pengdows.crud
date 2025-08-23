#region

using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

public class SqlContainer : SafeAsyncDisposableBase, ISqlContainer
{
    private readonly IDatabaseContext _context;
    private readonly ISqlDialect _dialect;

    private readonly ILogger<ISqlContainer> _logger;
    private readonly Dictionary<string, DbParameter> _parameters = new();

    internal SqlContainer(IDatabaseContext context, string? query = "", ILogger<ISqlContainer>? logger = null)
    {
        _context = context;
        _dialect = (context as ISqlDialectProvider)?.Dialect;
        _logger = logger ?? NullLogger<ISqlContainer>.Instance;
        Query = new StringBuilder(query ?? string.Empty);
    }

    public StringBuilder Query { get; }

    public int ParameterCount => _parameters.Count;

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

        _parameters.Add(parameter.ParameterName, parameter);
    }

    public DbParameter AddParameterWithValue<T>(DbType type, T value)
    {
        if (value is DbParameter)
        {
            throw new ArgumentException("Parameter type can't be DbParameter.");
        }

        return AddParameterWithValue(null, type, value);
    }

    public DbParameter AddParameterWithValue<T>(string? name, DbType type, T value)
    {
        name ??= GenerateRandomName();
        var parameter = _context.CreateDbParameter(name, type, value);
        _parameters.Add(name, parameter);
        return parameter;
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
    }

    public string WrapForStoredProc(ExecutionType executionType, bool includeParameters = true)
    {
        var procName = Query.ToString().Trim();

        if (string.IsNullOrWhiteSpace(procName))
            throw new InvalidOperationException("Procedure name is missing from the query.");

        var args = includeParameters ? BuildProcedureArguments() : string.Empty;

        return _context.ProcWrappingStyle switch
        {
            ProcWrappingStyle.PostgreSQL when executionType == ExecutionType.Read
                => $"SELECT * FROM {procName}({args})",

            ProcWrappingStyle.PostgreSQL
                => $"CALL {procName}({args})",

            ProcWrappingStyle.Oracle
                => $"BEGIN\n\t{procName}{(string.IsNullOrEmpty(args) ? string.Empty : $"({args})")};\nEND;",

            ProcWrappingStyle.Exec
                => string.IsNullOrWhiteSpace(args)
                    ? $"EXEC {procName}"
                    : $"EXEC {procName} {args}",

            ProcWrappingStyle.Call
                => $"CALL {procName}({args})",

            ProcWrappingStyle.ExecuteProcedure
                => $"EXECUTE PROCEDURE {procName}({args})",

            _ => throw new NotSupportedException("Stored procedures are not supported by this database.")
        };

        string BuildProcedureArguments()
        {
            if (_parameters.Count == 0)
                return string.Empty;

            // Named parameter support check
            if (_context.SupportsNamedParameters)
            {
                // Trust that dev has set correct names
                return string.Join(", ", _parameters.Values.Select(p => _dialect.MakeParameterName(p)));
            }

            // Positional binding (e.g., SQLite, MySQL)
            return string.Join(", ", Enumerable.Repeat("?", _parameters.Count));
        }
    }
    private string GenerateRandomName()
    {
        while (true)
        {
            var name = _context.GenerateRandomName();
            if (!_parameters.ContainsKey(name))
            {
                return name;
            }
        }
    }
    
    public async Task<int> ExecuteNonQueryAsync(CommandType commandType = CommandType.Text)
    {
        _context.AssertIsWriteConnection();
        ITrackedConnection? conn = null;
        DbCommand? cmd = null;
        try
        {
            await using var contextLocker = _context.GetLock();
            await contextLocker.LockAsync().ConfigureAwait(false);
            var isTransaction = _context is ITransactionContext;
            conn = _context.GetConnection(ExecutionType.Write, isTransaction);
            await using var connectionLocker = conn.GetLock();
            await connectionLocker.LockAsync().ConfigureAwait(false);
            cmd = PrepareCommand(conn, commandType, ExecutionType.Write);

            return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            Cleanup(cmd, conn, ExecutionType.Write);
        }
    }

    public async Task<T?> ExecuteScalarAsync<T>(CommandType commandType = CommandType.Text)
    {
        _context.AssertIsReadConnection();

        await using var reader = await ExecuteReaderAsync(commandType);
        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            var value = reader.GetValue(0); // always returns object
            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T?)TypeCoercionHelper.Coerce(value, reader.GetFieldType(0), targetType);
        }

        throw new InvalidOperationException("ExecuteScalarAsync expected at least one row but found none.");
    }

    public async Task<ITrackedReader> ExecuteReaderAsync(CommandType commandType = CommandType.Text)
    {
        _context.AssertIsReadConnection();

        ITrackedConnection conn;
        DbCommand cmd = null;
        try
        {
            await using var contextLocker = _context.GetLock();
            await contextLocker.LockAsync().ConfigureAwait(false);
            var isTransaction = _context is ITransactionContext;
            conn = _context.GetConnection(ExecutionType.Read, isTransaction);
            var connectionLocker = conn.GetLock();
            await connectionLocker.LockAsync().ConfigureAwait(false);
            cmd = PrepareCommand(conn, commandType, ExecutionType.Read);

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
            var dr = await cmd.ExecuteReaderAsync(behavior).ConfigureAwait(false);
            return new TrackedReader(dr, conn, connectionLocker, behavior == CommandBehavior.CloseConnection);
        }
        finally
        {
            //no matter what we do NOT close the underlying connection
            //or dispose it.
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


    private DbCommand PrepareCommand(ITrackedConnection conn, CommandType commandType, ExecutionType executionType)
    {
        if (commandType == CommandType.TableDirect)
        {
            throw new NotSupportedException("TableDirect isn't supported.");
        }

        if (Query.Length == 0)
        {
            throw new InvalidOperationException("SQL query is empty.");
        }

        OpenConnection(conn);
        var cmd = CreateCommand(conn);
        cmd.CommandType = CommandType.Text;
        _logger.LogInformation("Executing SQL: {Sql}", Query.ToString());
        if (_parameters.Count > 0 && _logger.IsEnabled(LogLevel.Debug))
        {
            var paramDump = string.Join(", ",
                _parameters.Values.Select(p => $"{p.ParameterName}={p.Value ?? "NULL"}"));
            _logger.LogDebug("Parameters: {Parameters}", paramDump);
        }
        cmd.CommandText = (commandType == CommandType.StoredProcedure)
            ? WrapForStoredProc(executionType)
            : Query.ToString();
        if (_parameters.Count > _context.MaxParameterLimit)
        {
            throw new InvalidOperationException(
                $"Query exceeds the maximum parameter limit of {_context.MaxParameterLimit} for {_context.DatabaseProductName}.");
        }

        foreach (var param in _parameters.Values)
        {
            cmd.Parameters.Add(param);
        }

        if (_context.PrepareStatements)
        {
            cmd.Prepare();
        }

        return cmd;
    }

    private void OpenConnection(ITrackedConnection conn)
    {
        if (conn.State != ConnectionState.Open)
        {
            conn.Open();
        }
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
        // Dispose managed resources here (clear parameters and query)

        _parameters.Clear();
        Query.Clear();
    }
}
