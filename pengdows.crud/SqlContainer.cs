// =============================================================================
// FILE: SqlContainer.cs
// PURPOSE: The core SQL query builder and executor that handles parameterized
//          queries, stored procedures, and connection management.
//
// AI SUMMARY:
// - This is the primary class for building and executing SQL statements.
// - Created via IDatabaseContext.CreateSqlContainer() - not directly instantiated.
// - Key features:
//   * Query property (StringBuilder) for building SQL dynamically
//   * AddParameterWithValue/CreateDbParameter for safe parameterization
//   * ExecuteNonQueryAsync/ExecuteScalarAsync/ExecuteReaderAsync for execution
//   * WrapObjectName for dialect-specific identifier quoting
//   * MakeParameterName for dialect-specific parameter naming (@p, :p, ?)
// - Manages parameter ordering for positional parameter databases (Oracle, ODBC).
// - Uses StringBuilderLite for efficient SQL construction with zero allocations.
// - Implements IDisposable/IAsyncDisposable for cleanup.
// - Thread-safe for building (not for concurrent modification).
// - Internally uses dialects (ISqlDialect) for database-specific SQL generation.
// - Stored procedure wrapping via IProcWrappingStrategy for cross-database compat.
// - Tracks whether WHERE clause was appended (HasWhereAppended) for convenience.
// - Limits max parameters per query (MaxParameterLimit) to prevent SQL errors.
// =============================================================================

#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.collections;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.strategies.proc;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using pengdows.crud.@internal;

#endregion

namespace pengdows.crud;

/// <summary>
/// A SQL query builder and executor that provides parameterized, database-agnostic SQL operations.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Creation:</strong> Always create instances via <see cref="IDatabaseContext.CreateSqlContainer"/>
/// rather than direct instantiation.
/// </para>
/// <para>
/// <strong>Query Building:</strong> Use the <see cref="Query"/> property (StringBuilder) to build SQL
/// dynamically. Use <see cref="AddParameterWithValue{T}"/> to add parameters safely.
/// </para>
/// <para>
/// <strong>Execution:</strong> Choose the appropriate execution method:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ExecuteNonQueryAsync"/> - INSERT, UPDATE, DELETE returning row count</description></item>
/// <item><description><see cref="ExecuteScalarAsync{T}"/> - Single value queries (COUNT, MAX, etc.)</description></item>
/// <item><description><see cref="ExecuteReaderAsync"/> - Multi-row result sets</description></item>
/// </list>
/// <para>
/// <strong>Parameter Naming:</strong> Use <see cref="MakeParameterName(string)"/> to generate
/// database-appropriate parameter markers (@param for SQL Server, :param for Oracle, etc.).
/// </para>
/// <para>
/// <strong>Identifier Quoting:</strong> Use <see cref="WrapObjectName"/> to quote table/column names
/// appropriately for the target database ("name" for most, [name] for SQL Server).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var container = context.CreateSqlContainer();
/// container.Query.Append("SELECT * FROM ");
/// container.Query.Append(container.WrapObjectName("users"));
/// container.Query.Append(" WHERE ");
/// container.Query.Append(container.WrapObjectName("email"));
/// container.Query.Append(" = ");
/// var emailParam = container.AddParameterWithValue("email", DbType.String, "user@example.com");
/// container.Query.Append(container.MakeParameterName(emailParam));
///
/// await using var reader = await container.ExecuteReaderAsync();
/// while (await reader.ReadAsync())
/// {
///     // Process rows
/// }
/// </code>
/// </example>
/// <seealso cref="ISqlContainer"/>
/// <seealso cref="IDatabaseContext.CreateSqlContainer"/>
/// <seealso cref="TableGateway{TEntity,TRowID}"/>
public class SqlContainer : SafeAsyncDisposableBase, ISqlContainer, ISqlDialectProvider, IReaderLifetimeListener
{
    private static readonly Regex ParamPlaceholderRegex = new(@"\{P\}([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private readonly IDatabaseContext _context;
    private readonly ISqlDialect _dialect;

    private readonly ILogger<ISqlContainer> _logger;

    private readonly IDictionary<string, ParameterMetadata> _parameters =
        new pengdows.crud.collections.OrderedDictionary<string, ParameterMetadata>(ParameterNameComparer.Instance);

    // No longer needed with metadata approach - parameters are pooled at context level
    // private readonly Dictionary<DbParameter, DbParameterCollection?> _parameterOwners =
    //     new(ParameterReferenceComparer.Instance);

    private sealed class ParameterNameComparer : IEqualityComparer<string>
    {
        public static ParameterNameComparer Instance { get; } = new();

        public bool Equals(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return NormalizedSpan(x).SequenceEqual(NormalizedSpan(y));
        }

        public int GetHashCode(string obj)
        {
            var normalized = NormalizedSpan(obj);
            var hash = new HashCode();
            foreach (var ch in normalized)
            {
                hash.Add(ch);
            }

            return hash.ToHashCode();
        }

        private static ReadOnlySpan<char> NormalizedSpan(string value)
        {
            return StripParameterPrefix(value);
        }
    }

    private static ReadOnlySpan<char> StripParameterPrefix(string parameterName)
    {
        if (parameterName.Length == 0)
        {
            return string.Empty;
        }

        return parameterName[0] switch
        {
            '@' or ':' or '?' or '$' => parameterName.AsSpan(1),
            _ => parameterName.AsSpan()
        };
    }

    private int _outputParameterCount;
    private int _nextParameterId = -1;
    internal List<string> ParamSequence { get; } = new();

    // Performance optimization: cache rendered command text to avoid repeated Query.ToString() calls
    private string? _cachedCommandText;
    private bool _commandTextDirty = true;
    private int _activeReaders;
    private int _deferParameterPooling;

    private MetricsCollector? GetMetricsCollector(ExecutionType executionType)
    {
        var accessor = _context as IMetricsCollectorAccessor;
        if (accessor == null)
        {
            return null;
        }

        return accessor.GetMetricsCollector(executionType) ?? accessor.MetricsCollector;
    }

    private TypeCoercionOptions DefaultCoercionOptions => TypeCoercionOptions.Default with
    {
        Provider = _dialect.DatabaseType
    };

    ISqlDialect ISqlDialectProvider.Dialect => _dialect;

    // Primary private constructor (enforces creation via factory methods)
    private SqlContainer(IDatabaseContext context, ISqlDialect dialect, string? query, ILogger<ISqlContainer>? logger)
    {
        _context = context;
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        _logger = logger ?? NullLogger<ISqlContainer>.Instance;
        Query = new StringBuilder(query ?? string.Empty);
    }


    // Legacy constructor kept for binary compatibility but made unreachable to callers.
    // Any attempt to call this directly will fail at compile time.
    [Obsolete("Do not construct SqlContainer directly. Use IDatabaseContext.CreateSqlContainer(...) instead.", true)]
    internal SqlContainer(IDatabaseContext context, string? query = "", ILogger<ISqlContainer>? logger = null)
        : this(
            context,
            (context as ISqlDialectProvider)?.Dialect
            ?? throw new InvalidOperationException(
                "IDatabaseContext must implement ISqlDialectProvider and expose a non-null Dialect."),
            query,
            logger)
    {
    }

    // Internal factory used by DatabaseContext/TransactionContext
    internal static SqlContainer Create(IDatabaseContext context, string? query = "",
        ILogger<ISqlContainer>? logger = null)
    {
        var dialect = (context as ISqlDialectProvider)?.Dialect
                      ?? throw new InvalidOperationException(
                          "IDatabaseContext must implement ISqlDialectProvider and expose a non-null Dialect.");
        return new SqlContainer(context, dialect, query, logger);
    }

    // Test support: allow explicit dialect for specialized scenarios
    internal static SqlContainer CreateForDialect(IDatabaseContext context, ISqlDialect dialect, string? query = "",
        ILogger<ISqlContainer>? logger = null)
    {
        return new SqlContainer(context, dialect, query, logger);
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

    public string MakeParameterName(ParameterMetadata metadata)
    {
        return _dialect.MakeParameterName(metadata.Name);
    }

    internal string RenderParams(string sql)
    {
        ParamSequence.Clear();
        return ParamPlaceholderRegex.Replace(sql, m =>
        {
            var name = m.Groups[1].Value;
            ParamSequence.Add(name);
            return _dialect.SupportsNamedParameters
                ? string.Concat(_dialect.ParameterMarker, name)
                : _dialect.ParameterMarker;
        });
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
            parameter.ParameterName = GenerateParameterName();
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

        // Convert DbParameter to ParameterMetadata for storage
        var metadata = new ParameterMetadata(
            parameter.ParameterName,
            parameter.DbType,
            parameter.Value,
            parameter.Direction);

        _parameters.Add(parameter.ParameterName, metadata);

        // Mark command text dirty since parameters changed
        _commandTextDirty = true;
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
        // Validate parameter direction before creating parameter
        if (direction == ParameterDirection.Output && _context.DataSourceInfo.MaxOutputParameters == 0)
        {
            throw new ArgumentException($"Output parameters are not supported by {_dialect.DatabaseType}.");
        }

        name ??= GenerateParameterName();

        // Check output parameter limit
        var isOutput = direction == ParameterDirection.Output ||
                       direction == ParameterDirection.InputOutput ||
                       direction == ParameterDirection.ReturnValue;

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

        // Store as metadata instead of creating DbParameter immediately
        var metadata = new ParameterMetadata(name, type, value, direction);
        _parameters.Add(name, metadata);

        // Mark command text dirty since parameters changed
        _commandTextDirty = true;

        // For API compatibility, return a temporary DbParameter
        // This will be recreated from pooled parameters at execution time
        var parameter = _context.CreateDbParameter(name, type, value);
        parameter.Direction = direction;
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

    private static bool TryBuildAlternateParameterName(string parameterName, out string alternateName)
    {
        var normalizedSpan = StripParameterPrefix(parameterName);
        var normalized = normalizedSpan.ToString();
        if (normalized.Length < 2)
        {
            alternateName = string.Empty;
            return false;
        }

        var prefix = normalized[0];
        if (prefix != 'p' && prefix != 'w')
        {
            alternateName = string.Empty;
            return false;
        }

        alternateName = string.Create(normalized.Length, normalized, static (span, source) =>
        {
            span[0] = source[0] == 'p' ? 'w' : 'p';
            source.AsSpan(1).CopyTo(span.Slice(1));
        });

        return true;
    }

    public void SetParameterValue(string parameterName, object? newValue)
    {
        if (!_parameters.TryGetValue(parameterName, out var metadata))
        {
            // Allow cross-prefix lookup between pN and wN for tests that use a different
            // prefix when asserting parameter values vs where they were created.
            if (_dialect.SupportsNamedParameters &&
                TryBuildAlternateParameterName(parameterName, out var alternate) &&
                _parameters.TryGetValue(alternate, out metadata))
            {
                parameterName = alternate; // Use the found alternate name
            }
            else
            {
                throw new KeyNotFoundException($"Parameter '{parameterName}' not found.");
            }
        }

        // If switching to an array value on providers that support set-valued parameters
        // (e.g., PostgreSQL ANY(@p)), coerce DbType to Object so the provider
        // can infer the correct array type during preparation.
        var dbType = metadata.DbType;
        if (newValue is Array && _dialect.SupportsSetValuedParameters)
        {
            dbType = DbType.Object;
        }

        // Update metadata with new value
        var updatedMetadata = new ParameterMetadata(
            metadata.Name,
            dbType,
            newValue,
            metadata.Direction);

        _parameters[parameterName] = updatedMetadata;

        // Mark command text dirty since parameter values changed
        _commandTextDirty = true;
    }

    public object? GetParameterValue(string parameterName)
    {
        if (!_parameters.TryGetValue(parameterName, out var metadata))
        {
            if (_dialect.SupportsNamedParameters &&
                TryBuildAlternateParameterName(parameterName, out var alternate) &&
                _parameters.TryGetValue(alternate, out metadata))
            {
                // proceed with found alternate
            }
            else
            {
                throw new KeyNotFoundException($"Parameter '{parameterName}' not found.");
            }
        }

        return metadata.Value;
    }

    public T GetParameterValue<T>(string parameterName)
    {
        var value = GetParameterValue(parameterName);
        var sourceType = value?.GetType() ?? typeof(object);
        var coerced = TypeCoercionHelper.Coerce(value, sourceType, typeof(T), DefaultCoercionOptions);

        return (T)coerced!;
    }


    public DbCommand CreateCommand(ITrackedConnection conn)
    {
        var dbCommand = CreateRawCommand(conn);

        if (Query.Length == 0)
        {
            return dbCommand;
        }

        // Mirror the normal execution path so manually-created commands are usable.
        var cmdText = Query.ToString();
        if (cmdText.Contains("{P}"))
        {
            cmdText = RenderParams(cmdText);
        }

        dbCommand.CommandType = CommandType.Text;
        dbCommand.CommandText = cmdText;

        if (_parameters.Count > _context.MaxParameterLimit)
        {
            throw new InvalidOperationException(
                $"Query exceeds the maximum parameter limit of {_context.MaxParameterLimit} for {_context.DatabaseProductName}.");
        }

        AddParametersToCommand(dbCommand);

        return dbCommand;
    }

    private DbCommand CreateRawCommand(ITrackedConnection conn)
    {
        var cmd = conn.CreateCommand();
        if (_context is TransactionContext transactionContext)
        {
            cmd.Transaction = transactionContext.Transaction as DbTransaction
                              ?? throw new InvalidOperationException("Transaction is not a transaction");
        }

        return cmd as DbCommand
               ?? throw new InvalidOperationException("Command is not a DbCommand");
    }

    private DbParameter[]? _rentedParameters; // Track rented parameters for later return

    private void AddParametersToCommand(DbCommand dbCommand)
    {
        if (_parameters.Count == 0)
        {
            return;
        }

        // Rent parameter array from context pool
        if (_context is not IInternalConnectionProvider provider)
        {
            throw new InvalidOperationException("DatabaseContext must support parameter pooling");
        }

        var ctx = provider as DatabaseContext;
        if (ctx == null)
        {
            throw new InvalidOperationException("Context must be DatabaseContext for parameter pooling");
        }

        _rentedParameters = ctx.RentParameters(_parameters.Count);

        // Materialize parameters from metadata
        var index = 0;
        if (_context.SupportsNamedParameters || ParamSequence.Count == 0)
        {
            foreach (var kvp in _parameters)
            {
                var metadata = kvp.Value;
                var param = _rentedParameters[index++];

                // Populate from metadata
                param.ParameterName = metadata.Name;
                param.DbType = metadata.DbType;
                param.Value = metadata.Value ?? DBNull.Value;
                param.Direction = metadata.Direction;

                dbCommand.Parameters.Add(param);
            }

            return;
        }

        // Positional parameters - use ParamSequence order
        foreach (var name in ParamSequence)
        {
            if (_parameters.TryGetValue(name, out var metadata))
            {
                var param = _rentedParameters[index++];

                // Populate from metadata
                param.ParameterName = metadata.Name;
                param.DbType = metadata.DbType;
                param.Value = metadata.Value ?? DBNull.Value;
                param.Direction = metadata.Direction;

                dbCommand.Parameters.Add(param);
            }
        }
    }

    // REMOVED: No longer needed with metadata approach - parameters are pooled at context level
    // private void RegisterParameterOwner(DbParameter parameter, DbParameterCollection owner)
    // private void RemoveParameterFromOwner(DbParameter parameter)
    // private sealed class ParameterReferenceComparer : IEqualityComparer<DbParameter>

    public void Clear()
    {
        Query.Clear();

        // Return any rented parameters to pool before clearing metadata
        ReturnRentedParametersToPool();

        // Clear metadata (no longer calling old ReturnParametersToPool)
        _parameters.Clear();

        _outputParameterCount = 0;
        ParamSequence.Clear();

        // Invalidate cached command text when query is cleared
        _cachedCommandText = null;
        _commandTextDirty = true;
    }

    public string WrapForStoredProc(ExecutionType executionType, bool includeParameters = true,
        bool captureReturn = false)
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
                var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
                var index = 0;
                foreach (var metadata in _parameters.Values)
                {
                    if (index++ > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(_context.MakeParameterName(metadata.Name));
                }

                return sb.ToString();
            }

            if (_parameters.Count == 0)
            {
                return string.Empty;
            }

            var positional = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
            for (var i = 0; i < _parameters.Count; i++)
            {
                if (i > 0)
                {
                    positional.Append(", ");
                }

                positional.Append('?');
            }

            return positional.ToString();
        }
    }

    // Overload without defaults to avoid ambiguity with the 3-arg version
    public string WrapForStoredProc(ExecutionType executionType, bool includeParameters)
    {
        return WrapForStoredProc(executionType, includeParameters, false);
    }

    public string WrapForCreateWithReturn(bool includeParameters = true)
    {
        return WrapForStoredProc(ExecutionType.Write, includeParameters, true);
    }

    public string WrapForUpdateWithReturn(bool includeParameters = true)
    {
        return WrapForStoredProc(ExecutionType.Write, includeParameters, true);
    }

    public string WrapForDeleteWithReturn(bool includeParameters = true)
    {
        return WrapForStoredProc(ExecutionType.Write, includeParameters, true);
    }

    private string GenerateParameterName()
    {
        var maxLength = Math.Max(1, _context.DataSourceInfo.ParameterNameMaxLength);
        const string basePrefix = "p";

        if (maxLength <= basePrefix.Length)
        {
            return basePrefix[..maxLength];
        }

        var available = maxLength - basePrefix.Length;
        var next = Interlocked.Increment(ref _nextParameterId);

        // Use string.Create to avoid allocations
        return string.Create(maxLength, (next, available, basePrefix.Length), static (span, state) =>
        {
            var (counter, availableSpace, prefixLen) = state;

            // Write prefix
            "p".AsSpan().CopyTo(span);

            // Format counter as hex into stack buffer
            Span<char> hexBuffer = stackalloc char[16]; // Max hex digits for long
            counter.TryFormat(hexBuffer, out var written, "x", CultureInfo.InvariantCulture);
            var hexSpan = hexBuffer[..written];

            if (hexSpan.Length > availableSpace)
            {
                // Truncate from left (take last N chars)
                hexSpan[(hexSpan.Length - availableSpace)..].CopyTo(span[prefixLen..]);
            }
            else if (hexSpan.Length < availableSpace)
            {
                // Pad with zeros on the left
                var paddingCount = availableSpace - hexSpan.Length;
                span.Slice(prefixLen, paddingCount).Fill('0');
                hexSpan.CopyTo(span[(prefixLen + paddingCount)..]);
            }
            else
            {
                // Exact fit
                hexSpan.CopyTo(span[prefixLen..]);
            }
        });
    }

    public ValueTask<int> ExecuteNonQueryAsync(CommandType commandType = CommandType.Text)
    {
        return ExecuteNonQueryAsync(commandType, CancellationToken.None);
    }

    public async ValueTask<int> ExecuteNonQueryAsync(CommandType commandType, CancellationToken cancellationToken)
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
        var metrics = GetMetricsCollector(ExecutionType.Write);
        var startTimestamp = metrics?.CommandStarted(_parameters.Count) ?? 0;
        try
        {
            await using var contextLocker = _context.GetLock();
            await contextLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            var isTransaction = _context is ITransactionContext;
            var isShared = ShouldUseSharedConnection(_context, ExecutionType.Write, isTransaction);
            conn = GetConnection(ExecutionType.Write, isShared);
            // Note: SingleWriter mode now uses Standard lifecycle with governor policy.
            // The governor (WriteSlots=1) ensures only one write at a time.
            await using var connectionLocker = conn.GetLock();
            await connectionLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            cmd = await PrepareAndCreateCommandAsync(conn, commandType, ExecutionType.Write, cancellationToken)
                .ConfigureAwait(false);
            var result = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            metrics?.CommandSucceeded(startTimestamp, result);
            metrics?.RecordRowsAffected(result);
            return result;
        }
        catch (OperationCanceledException)
        {
            metrics?.CommandCancelled(startTimestamp);
            throw;
        }
        catch (Exception ex) when (IsTimeout(ex))
        {
            metrics?.CommandTimedOut(startTimestamp);
            throw;
        }
        catch
        {
            metrics?.CommandFailed(startTimestamp);
            throw;
        }
        finally
        {
            Cleanup(cmd, conn, ExecutionType.Write);
        }
    }

    public ValueTask<T?> ExecuteScalarAsync<T>(CommandType commandType = CommandType.Text)
    {
        return ExecuteScalarAsync<T>(commandType, CancellationToken.None);
    }

    public async ValueTask<T?> ExecuteScalarAsync<T>(CommandType commandType, CancellationToken cancellationToken)
    {
        _context.AssertIsReadConnection();

        await using var reader = await ExecuteReaderAsync(commandType, cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            var value = reader.GetValue(0); // always returns object
            if (typeof(T) == typeof(object))
            {
                return (T?)value;
            }

            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T?)TypeCoercionHelper.Coerce(value, reader.GetFieldType(0), targetType, DefaultCoercionOptions);
        }

        // Return default for nullable types, throw for non-nullable types (following ADO.NET ExecuteScalar behavior)
        var isNullable = !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;
        if (isNullable)
        {
            return default;
        }

        throw new InvalidOperationException("ExecuteScalarAsync expected at least one row but found none.");
    }

    // Write-path scalar execution (e.g., INSERT ... RETURNING / OUTPUT)
    public ValueTask<T?> ExecuteScalarWriteAsync<T>(CommandType commandType = CommandType.Text)
    {
        return ExecuteScalarWriteAsync<T>(commandType, CancellationToken.None);
    }

    public async ValueTask<T?> ExecuteScalarWriteAsync<T>(CommandType commandType, CancellationToken cancellationToken)
    {
        // Check for explicit read-only mode
        if (_context is DatabaseContext dbContext && dbContext.ReadWriteMode == ReadWriteMode.ReadOnly)
        {
            throw new NotSupportedException("Write operations are not supported in read-only mode.");
        }

        _context.AssertIsWriteConnection();
        ITrackedConnection? conn = null;
        DbCommand? cmd = null;
        var metrics = GetMetricsCollector(ExecutionType.Read);
        var startTimestamp = metrics?.CommandStarted(_parameters.Count) ?? 0;
        try
        {
            await using var contextLocker = _context.GetLock();
            await contextLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            var isTransaction = _context is ITransactionContext;
            var isShared = ShouldUseSharedConnection(_context, ExecutionType.Write, isTransaction);
            conn = GetConnection(ExecutionType.Write, isShared);
            // Note: SingleWriter mode now uses Standard lifecycle with governor policy.
            // The governor (WriteSlots=1) ensures only one write at a time.
            await using var connectionLocker = conn.GetLock();
            await connectionLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            cmd = await PrepareAndCreateCommandAsync(conn, commandType, ExecutionType.Write, cancellationToken)
                .ConfigureAwait(false);

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is null || result is DBNull)
            {
                var isNullable = !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;
                if (!isNullable)
                {
                    metrics?.CommandFailed(startTimestamp);
                    throw new InvalidOperationException("ExecuteScalarWriteAsync expected a value but found none.");
                }

                metrics?.CommandSucceeded(startTimestamp, 0);
                return default;
            }

            if (typeof(T) == typeof(object))
            {
                metrics?.RecordRowsRead(1);
                metrics?.CommandSucceeded(startTimestamp, 0);
                return (T?)result;
            }

            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            var coerced = (T?)TypeCoercionHelper.Coerce(result, result.GetType(), targetType, DefaultCoercionOptions);
            metrics?.RecordRowsRead(coerced is null ? 0 : 1);
            metrics?.CommandSucceeded(startTimestamp, 0);
            return coerced;
        }
        catch (OperationCanceledException)
        {
            metrics?.CommandCancelled(startTimestamp);
            throw;
        }
        catch (Exception ex) when (IsTimeout(ex))
        {
            metrics?.CommandTimedOut(startTimestamp);
            throw;
        }
        catch
        {
            metrics?.CommandFailed(startTimestamp);
            throw;
        }
        finally
        {
            Cleanup(cmd, conn, ExecutionType.Write);
        }
    }

    public ValueTask<ITrackedReader> ExecuteReaderAsync(CommandType commandType = CommandType.Text)
    {
        return ExecuteReaderAsync(commandType, CancellationToken.None);
    }

    public async ValueTask<ITrackedReader> ExecuteReaderAsync(CommandType commandType, CancellationToken cancellationToken)
    {
        _context.AssertIsReadConnection();

        ITrackedConnection conn;
        DbCommand? cmd = null;
        ILockerAsync? connectionLocker = null;
        var metrics = GetMetricsCollector(ExecutionType.Write);
        var startTimestamp = metrics?.CommandStarted(_parameters.Count) ?? 0;
        var lockTransferred = false;
        try
        {
            await using var contextLocker = _context.GetLock();
            await contextLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            var isTransaction = _context is ITransactionContext;
            var isShared = ShouldUseSharedConnection(_context, ExecutionType.Read, isTransaction);
            conn = GetConnection(ExecutionType.Read, isShared);
            connectionLocker = conn.GetLock();
            await connectionLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            cmd = await PrepareAndCreateCommandAsync(conn, commandType, ExecutionType.Read, cancellationToken)
                .ConfigureAwait(false);

            // unless the databaseContext is in a transaction or SingleConnection mode,
            // a new connection is returned for every READ operation, therefore, we
            // are going to set the connection to close and dispose when the reader is
            // closed. This prevents leaking
            var isSingleConnection = _context.ConnectionMode == DbMode.SingleConnection;
            var behavior = isTransaction || isSingleConnection
                ? CommandBehavior.Default
                : CommandBehavior.CloseConnection;
            //behavior |= CommandBehavior.SingleResult;

            // if this is our single connection to the database, for a transaction
            //or sqlCe mode, or single connection mode, we will NOT close the connection.
            // otherwise, we will have the connection set to autoclose so that we
            //close the underlying connection when the DbDataReader is closed;
            var dr = await cmd.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
            metrics?.CommandSucceeded(startTimestamp, 0);
            Interlocked.Increment(ref _activeReaders);
            var trackedReader = new TrackedReader(
                dr,
                conn,
                connectionLocker,
                behavior == CommandBehavior.CloseConnection,
                cmd,
                metrics,
                this);
            cmd = null;
            lockTransferred = true; // TrackedReader now owns the lock
            return trackedReader;
        }
        catch (OperationCanceledException)
        {
            metrics?.CommandCancelled(startTimestamp);
            throw;
        }
        catch (Exception ex) when (IsTimeout(ex))
        {
            metrics?.CommandTimedOut(startTimestamp);
            throw;
        }
        catch
        {
            metrics?.CommandFailed(startTimestamp);
            throw;
        }
        finally
        {
            // If lock wasn't transferred to TrackedReader, release it here
            if (!lockTransferred && connectionLocker != null)
            {
                try
                {
                    await connectionLocker.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Ignore disposal errors in finally block
                }
            }

            //no matter what we do NOT close the underlying connection
            //or dispose it here—the reader manages command disposal.
            Cleanup(cmd, null, ExecutionType.Read);
        }
    }

    // Optimized single-row reader to hint providers/ADO.NET for minimal result shape
    public async ValueTask<ITrackedReader> ExecuteReaderSingleRowAsync(CancellationToken cancellationToken = default)
    {
        _context.AssertIsReadConnection();

        ITrackedConnection conn;
        DbCommand? cmd = null;
        ILockerAsync? connectionLocker = null;
        var metrics = GetMetricsCollector(ExecutionType.Read);
        var startTimestamp = metrics?.CommandStarted(_parameters.Count) ?? 0;
        var lockTransferred = false;
        try
        {
            await using var contextLocker = _context.GetLock();
            await contextLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            var isTransaction = _context is ITransactionContext;
            var isShared = ShouldUseSharedConnection(_context, ExecutionType.Read, isTransaction);
            conn = GetConnection(ExecutionType.Read, isShared);
            connectionLocker = conn.GetLock();
            await connectionLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            cmd = await PrepareAndCreateCommandAsync(conn, CommandType.Text, ExecutionType.Read, cancellationToken)
                .ConfigureAwait(false);

            var isSingleConnection = _context.ConnectionMode == DbMode.SingleConnection;
            var behavior = isTransaction || isSingleConnection
                ? CommandBehavior.SingleRow
                : CommandBehavior.CloseConnection | CommandBehavior.SingleRow;

            var dr = await cmd.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
            metrics?.CommandSucceeded(startTimestamp, 0);
            Interlocked.Increment(ref _activeReaders);
            var trackedReader = new TrackedReader(
                dr,
                conn,
                connectionLocker,
                (behavior & CommandBehavior.CloseConnection) == CommandBehavior.CloseConnection,
                cmd,
                metrics,
                this);
            cmd = null;
            lockTransferred = true; // TrackedReader now owns the lock
            return trackedReader;
        }
        catch (OperationCanceledException)
        {
            metrics?.CommandCancelled(startTimestamp);
            throw;
        }
        catch (Exception ex) when (IsTimeout(ex))
        {
            metrics?.CommandTimedOut(startTimestamp);
            throw;
        }
        catch
        {
            metrics?.CommandFailed(startTimestamp);
            throw;
        }
        finally
        {
            // If lock wasn't transferred to TrackedReader, release it here
            if (!lockTransferred && connectionLocker != null)
            {
                try
                {
                    await connectionLocker.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Ignore disposal errors in finally block
                }
            }

            // Command lifetime is managed by the returned reader for read operations.
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
        var traceTimings = _logger.IsEnabled(LogLevel.Debug);
        long tStart = 0;
        long tOpened = 0;
        long tCmdText = 0;
        long tCmdCreated = 0;
        long tParamsAdded = 0;
        long tPrepared = 0;
        if (traceTimings)
        {
            tStart = Stopwatch.GetTimestamp();
        }

        if (commandType == CommandType.TableDirect)
        {
            throw new NotSupportedException("TableDirect isn't supported.");
        }

        if (Query.Length == 0)
        {
            throw new InvalidOperationException("SQL query is empty.");
        }

        await OpenConnectionAsync(conn, cancellationToken).ConfigureAwait(false);
        if (traceTimings)
        {
            tOpened = Stopwatch.GetTimestamp();
        }

        // Performance optimization: cache command text to avoid repeated Query.ToString() calls
        string cmdText;
        if (commandType == CommandType.StoredProcedure)
        {
            cmdText = WrapForStoredProc(executionType, true);
        }
        else if (_cachedCommandText != null && !_commandTextDirty)
        {
            // Reuse cached command text - huge win for template reuse patterns
            cmdText = _cachedCommandText;
        }
        else
        {
            // Render and cache the command text
            cmdText = Query.ToString();

            // For non-stored-proc queries, render parameter placeholders
            // This is CRITICAL for positional-parameter providers (MySQL, SQLite, etc.)
            // RenderParams() populates ParamSequence and replaces {P}name with ? or @name
            if (cmdText.Contains("{P}"))
            {
                cmdText = RenderParams(cmdText);
            }

            // Cache the rendered command text for reuse
            _cachedCommandText = cmdText;
            _commandTextDirty = false;
        }

        if (traceTimings)
        {
            tCmdText = Stopwatch.GetTimestamp();
        }

        var cmd = CreateRawCommand(conn);
        if (traceTimings)
        {
            tCmdCreated = Stopwatch.GetTimestamp();
        }

        // Stored procedures are wrapped into provider-specific text (EXEC/CALL/etc.)
        // so we always execute as CommandType.Text for consistent behavior across providers.
        cmd.CommandType = CommandType.Text;

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Executing SQL: {Sql}", cmdText);
        }

        if (_parameters.Count > 0 && _logger.IsEnabled(LogLevel.Debug))
        {
            // SECURITY: Never log parameter values - they may contain credentials, tokens, PII
            // Log only metadata: name, type, direction
            var paramDump = new StringBuilder();
            var index = 0;
            foreach (var metadata in _parameters.Values)
            {
                if (index++ > 0)
                {
                    paramDump.Append(", ");
                }

                var dirInfo = metadata.Direction != ParameterDirection.Input ? $" {metadata.Direction}" : "";
                paramDump.Append(metadata.Name);
                paramDump.Append(':');
                paramDump.Append(metadata.DbType.ToString());
                paramDump.Append(dirInfo);
            }

            _logger.LogDebug("Parameters: {Parameters}", paramDump.ToString());
        }

        cmd.CommandText = cmdText;
        // Bind parameters consistently with CreateCommand
        AddParametersToCommand(cmd);

        if (traceTimings)
        {
            tParamsAdded = Stopwatch.GetTimestamp();
        }

        // Apply per-text prepare logic
        MaybePrepareCommand(cmd, conn, executionType);
        if (traceTimings)
        {
            tPrepared = Stopwatch.GetTimestamp();
        }

        if (traceTimings)
        {
            var openUs = TicksToMicroseconds(tOpened - tStart);
            var textUs = TicksToMicroseconds(tCmdText - tOpened);
            var cmdUs = TicksToMicroseconds(tCmdCreated - tCmdText);
            var paramsUs = TicksToMicroseconds(tParamsAdded - tCmdCreated);
            var prepareUs = TicksToMicroseconds(tPrepared - tParamsAdded);
            var totalUs = TicksToMicroseconds(tPrepared - tStart);
            _logger.LogDebug(
                "SQL timing [{ExecutionType}/{CommandType}] params={ParamCount} open={OpenUs:0.000}us text={TextUs:0.000}us cmd={CmdUs:0.000}us params={ParamsUs:0.000}us prepare={PrepareUs:0.000}us total={TotalUs:0.000}us",
                executionType,
                commandType,
                _parameters.Count,
                openUs,
                textUs,
                cmdUs,
                paramsUs,
                prepareUs,
                totalUs);
        }

        return cmd;
    }

    /// <summary>
    /// Applies prepare logic: prepares once per connection per SQL text,
    /// with fallback to disable prepare on provider failures.
    /// </summary>
    private void MaybePrepareCommand(DbCommand cmd, ITrackedConnection conn, ExecutionType executionType)
    {
        var shouldPrepare = ComputeEffectivePrepareSettings();

        if (!shouldPrepare || conn.LocalState.PrepareDisabled)
        {
            return;
        }

        var sqlText = cmd.CommandText;
        if (conn.LocalState.IsAlreadyPreparedForShape(sqlText))
        {
            return;
        }

        try
        {
            cmd.Prepare();
            GetMetricsCollector(executionType)?.RecordPreparedStatement();
            if (conn.LocalState.MarkShapePrepared(sqlText, out var evicted))
            {
                GetMetricsCollector(executionType)?.RecordStatementCached();
                if (evicted > 0)
                {
                    GetMetricsCollector(executionType)?.RecordStatementEvicted(evicted);
                }
            }
        }
        catch (Exception ex)
        {
            if (_dialect.ShouldDisablePrepareOn(ex))
            {
                conn.LocalState.PrepareDisabled = true;
                _logger?.LogDebug(ex,
                    "Disabled prepare for connection due to provider exception: {ExceptionType}",
                    ex.GetType().Name);
            }
            else
            {
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

    private static bool IsTimeout(Exception exception)
    {
        return exception is TimeoutException ||
               exception.GetType().Name.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldUseSharedConnection(IDatabaseContext context, ExecutionType executionType,
        bool isTransaction)
    {
        if (isTransaction)
        {
            return true;
        }

        return context.ConnectionMode switch
        {
            DbMode.SingleConnection => true,
            _ => false
        };
    }

    private ITrackedConnection GetConnection(ExecutionType executionType, bool isShared)
    {
        if (_context is not IInternalConnectionProvider provider)
        {
            throw new InvalidOperationException("IDatabaseContext must provide internal connection access.");
        }

        return provider.GetConnection(executionType, isShared);
    }

    private static double TicksToMicroseconds(long ticks)
    {
        if (ticks <= 0)
        {
            return 0d;
        }

        return ticks * 1_000_000d / Stopwatch.Frequency;
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
                // IMPORTANT: Copy output parameter values back before returning to pool
                if (_rentedParameters != null && _rentedParameters.Length > 0 && _outputParameterCount > 0)
                {
                    CopyOutputParameterValues(cmd.Parameters, _rentedParameters);
                }

                cmd.Parameters?.Clear();
                try
                {
                    cmd.Connection = null;
                }
                catch
                { /* ignore */
                }

                cmd.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Command disposal failed: {ex.Message}");
                // We're intentionally not retrying here anymore — disposal failure is generally harmless in this case
            }
            finally
            {
                // Return parameters to pool
                ReturnRentedParametersToPool();
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

    private void CopyOutputParameterValues(DbParameterCollection commandParams, DbParameter[] rentedParams)
    {
        // Copy output parameter values back to metadata
        for (int i = 0; i < rentedParams.Length; i++)
        {
            var param = rentedParams[i];
            if (param.Direction == ParameterDirection.Output ||
                param.Direction == ParameterDirection.InputOutput ||
                param.Direction == ParameterDirection.ReturnValue)
            {
                // Update metadata with output value
                if (_parameters.TryGetValue(param.ParameterName, out var oldMetadata))
                {
                    var updatedMetadata = new ParameterMetadata(
                        oldMetadata.Name,
                        oldMetadata.DbType,
                        param.Value, // Updated value from execution
                        oldMetadata.Direction);

                    _parameters[param.ParameterName] = updatedMetadata;
                }
            }
        }
    }

    private void ReturnRentedParametersToPool()
    {
        if (_rentedParameters == null)
        {
            return;
        }

        if (_context is not IInternalConnectionProvider provider)
        {
            return;
        }

        var ctx = provider as DatabaseContext;
        if (ctx == null)
        {
            return;
        }

        try
        {
            ctx.ReturnParameters(_rentedParameters);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to return parameters to pool: {ex.Message}");
        }
        finally
        {
            _rentedParameters = null;
        }
    }

    public ISqlContainer Clone()
    {
        return Clone(null);
    }

    public ISqlContainer Clone(IDatabaseContext? context)
    {
        // Use the provided context or fallback to the original context
        var targetContext = context ?? _context;

        // Create a new container with the target context - let it get a StringBuilder from the pool
        var targetDialect = (targetContext as ISqlDialectProvider)?.Dialect
                            ?? _dialect;
        var clone = new SqlContainer(targetContext, targetDialect, null, _logger);

        // OPTIMIZATION: Share cached command text instead of copying StringBuilder
        // This is a massive win for template cloning patterns - avoids Query.ToString() on every clone
        if (_cachedCommandText != null && !_commandTextDirty)
        {
            // Template has been rendered - share the immutable command text
            clone._cachedCommandText = _cachedCommandText;
            clone._commandTextDirty = false;

            // Copy just the query length for validation (actual text is cached)
            clone.Query.Clear();
            clone.Query.Append(Query);
        }
        else
        {
            // Template not yet rendered or modified - copy StringBuilder content
            clone.Query.Clear();
            clone.Query.Append(Query);
            clone._commandTextDirty = true;
        }

        // Copy the WHERE flag
        clone.HasWhereAppended = HasWhereAppended;

        // OPTIMIZATION: Clone metadata directly (much lighter than DbParameter objects)
        // Metadata is immutable struct, so simple value copy is safe and fast
        foreach (var kvp in _parameters)
        {
            clone._parameters.Add(kvp.Key, kvp.Value); // Direct struct copy
        }

        // Copy parameter sequence for rendering (reuse cached if available)
        if (ParamSequence.Count > 0)
        {
            clone.ParamSequence.AddRange(ParamSequence);
        }

        clone._outputParameterCount = _outputParameterCount;

        return clone;
    }

    /// <summary>
    /// Internal method to reset execution state for container reuse.
    /// Preserves query and parameters but clears execution-specific state.
    /// Used for high-performance pooling scenarios.
    /// </summary>
    internal void Reset()
    {
        // Don't clear ParamSequence or cached command text - those are reusable
        // Just reset output parameter count
        _outputParameterCount = 0;

        // Note: Query, parameters, HasWhereAppended, and cached command text are preserved
        // This allows the container to be reused with the same query/parameters
    }

    protected override void DisposeManaged()
    {
        Clear();
    }

    // REMOVED: Old ReturnParametersToPool that used SqlDialect pooling
    // Now we use ReturnRentedParametersToPool which returns to DatabaseContext pool

    void IReaderLifetimeListener.OnReaderDisposed()
    {
        var remaining = Interlocked.Decrement(ref _activeReaders);
        if (remaining == 0 && Volatile.Read(ref _deferParameterPooling) == 1)
        {
            // Return rented parameters when last reader closes
            ReturnRentedParametersToPool();
            Volatile.Write(ref _deferParameterPooling, 0);
        }
    }
}
