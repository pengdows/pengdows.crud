#region

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.fakeDb;

public class fakeDbConnection : DbConnection, IDbConnection, IDisposable, IAsyncDisposable
{
    private string? _connectionString;
    private SupportedDatabase? _emulatedProduct;
    private DataTable? _schemaTable;
    private ConnectionState _state = ConnectionState.Closed;
    private string _serverVersion = "1.0";
    private int? _maxParameterLimit;
    private bool _shouldFailOnOpen;
    private bool _shouldFailOnCommand;
    private bool _shouldFailOnBeginTransaction;
    private Exception? _closeFailureException;
    private Exception? _customFailureException;
    private int _openCallCount;
    private int? _failAfterOpenCount;
    private fakeDbFactory? _sharedFactory;
    private int? _sharedFailAfterOpenCount;
    private bool _isBroken;
    private bool _skipFirstFailOnOpen;
    private fakeDbFactory? _factoryRef;
    private string? _emulatedTypeName;
    public override string DataSource => "FakeSource";
    public override string ServerVersion => GetEmulatedServerVersion();

    internal readonly Queue<IEnumerable<Dictionary<string, object?>>> ReaderResults = new();
    public readonly Queue<object?> ScalarResults = new();
    public readonly Queue<int> NonQueryResults = new();
    internal readonly Dictionary<string, object?> ScalarResultsByCommand = new();
    internal Exception? NonQueryExecuteException { get; private set; }
    internal Exception? ScalarExecuteException { get; private set; }
    internal Exception? PersistentScalarException { get; private set; }
    internal object? DefaultScalarResultOnce { get; private set; }
    internal readonly Dictionary<string, Exception> CommandFailuresByText = new();
    public readonly List<string> ExecutedNonQueryTexts = new();
    public readonly List<string> ExecutedReaderTexts = new();

    // Enhanced data persistence
    internal readonly FakeDataStore DataStore = new();
    /// <summary>
    /// Controls whether the connection should persist DML results in-memory for subsequent queries.
    /// Tests opt-in explicitly to avoid surprising behavior changes in existing suites.
    /// </summary>
    public bool EnableDataPersistence { get; set; } = false;

    public void EnqueueReaderResult(IEnumerable<Dictionary<string, object?>> rows)
    {
        ReaderResults.Enqueue(rows);
    }

    public void EnqueueScalarResult(object? value)
    {
        ScalarResults.Enqueue(value);
    }

    public void EnqueueNonQueryResult(int value)
    {
        NonQueryResults.Enqueue(value);
    }

    public void SetScalarResultForCommand(string commandText, object? value)
    {
        ScalarResultsByCommand[commandText] = value;
    }

    public void SetNonQueryExecuteException(Exception? exception)
    {
        NonQueryExecuteException = exception;
    }

    public void SetScalarExecuteException(Exception? exception)
    {
        ScalarExecuteException = exception;
    }

    public void SetPersistentScalarException(Exception? exception)
    {
        PersistentScalarException = exception;
    }

    public void SetDefaultScalarOnce(object? value)
    {
        DefaultScalarResultOnce = value;
    }

    internal object? ConsumeDefaultScalarOnce()
    {
        var v = DefaultScalarResultOnce;
        DefaultScalarResultOnce = null;
        return v;
    }

    public void SetCommandFailure(string commandText, Exception exception)
    {
        CommandFailuresByText[commandText] = exception;
    }

    public void SetServerVersion(string version)
    {
        _serverVersion = version;
    }

    public void SetMaxParameterLimit(int limit)
    {
        _maxParameterLimit = limit;
    }

    public int? GetMaxParameterLimit()
    {
        return _maxParameterLimit;
    }

    /// <summary>
    /// Sets the emulated type name for the connection (e.g., "Npgsql.NpgsqlConnection")
    /// This affects GetType().FullName behavior to simulate different connection types
    /// </summary>
    public void SetEmulatedTypeName(string typeName)
    {
        _emulatedTypeName = typeName;
    }

    /// <summary>
    /// Gets the emulated type name if set, otherwise returns the actual type name
    /// </summary>
    public string GetEmulatedTypeName()
    {
        return _emulatedTypeName ?? GetType().FullName ?? "fakeDbConnection";
    }

    /// <summary>
    /// Override for testing purposes - checks if the type name starts with the specified prefix
    /// This is used by dialects to check connection types (e.g., "Npgsql.")
    /// </summary>
    public bool TypeNameStartsWith(string prefix)
    {
        var typeName = _emulatedTypeName ?? GetType().FullName ?? "";
        return typeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sets the connection to fail on the next Open() or OpenAsync() call
    /// </summary>
    public void SetFailOnOpen(bool shouldFail = true, bool skipFirstOpen = false)
    {
        _shouldFailOnOpen = shouldFail;
        _skipFirstFailOnOpen = skipFirstOpen;
    }

    /// <summary>
    /// Sets the connection to fail when creating commands
    /// </summary>
    public void SetFailOnCommand(bool shouldFail = true)
    {
        _shouldFailOnCommand = shouldFail;
    }

    /// <summary>
    /// Sets the connection to fail when beginning transactions
    /// </summary>
    public void SetFailOnBeginTransaction(bool shouldFail = true)
    {
        _shouldFailOnBeginTransaction = shouldFail;
    }

    /// <summary>
    /// Sets a custom exception to throw instead of the default InvalidOperationException
    /// </summary>
    public void SetCustomFailureException(Exception exception)
    {
        _customFailureException = exception;
    }

    /// <summary>
    /// Sets the connection to fail after N successful open operations
    /// </summary>
    public void SetFailAfterOpenCount(int openCount)
    {
        _failAfterOpenCount = openCount;
        _openCallCount = 0;
    }

    /// <summary>
    /// Sets the connection to fail after N successful open operations across the entire factory
    /// </summary>
    internal void SetSharedFailAfterOpenCount(fakeDbFactory factory, int openCount)
    {
        _sharedFactory = factory;
        _sharedFailAfterOpenCount = openCount;
    }

    /// <summary>
    /// Sets a reference to the factory for factory-level failure coordination
    /// </summary>
    internal void SetFactoryReference(fakeDbFactory factory)
    {
        _factoryRef = factory;
    }

    /// <summary>
    /// Simulates a broken connection by setting state to Broken
    /// </summary>
    public void BreakConnection(bool skipFirst = false)
    {
        if (!skipFirst)
        {
            var original = _state;
            _state = ConnectionState.Broken;
            _isBroken = true;
            RaiseStateChangedEvent(original);
        }
        else
        {
            // Mark as broken but don't change state yet - factory will control when it breaks
            _isBroken = true;
        }
    }

    /// <summary>
    /// Resets all failure conditions
    /// </summary>
    public void ResetFailureConditions()
    {
        _shouldFailOnOpen = false;
        _shouldFailOnCommand = false;
        _shouldFailOnBeginTransaction = false;
        _customFailureException = null;
        _failAfterOpenCount = null;
        _sharedFactory = null;
        _sharedFailAfterOpenCount = null;
        _openCallCount = 0;
        _isBroken = false;
        _skipFirstFailOnOpen = false;
        _factoryRef = null;
    }

    private string GetEmulatedServerVersion()
    {
        if (!string.IsNullOrEmpty(_serverVersion) && _serverVersion != "1.0")
        {
            return _serverVersion;
        }

        return EmulatedProduct switch
        {
            SupportedDatabase.SqlServer => "Microsoft SQL Server 2019",
            SupportedDatabase.PostgreSql => "PostgreSQL 15.0",
            SupportedDatabase.MySql => "8.0.33",
            SupportedDatabase.MariaDb => "10.11.0",
            SupportedDatabase.Sqlite => "3.42.0",
            SupportedDatabase.Oracle => "Oracle Database 19c",
            SupportedDatabase.Firebird => "4.0.0",
            SupportedDatabase.CockroachDb => "v23.1.0",
            SupportedDatabase.DuckDB => "DuckDB 0.9.2",
            _ => "1.0"
        };
    }

    public SupportedDatabase EmulatedProduct
    {
        get
        {
            _emulatedProduct ??= SupportedDatabase.Unknown;
            return _emulatedProduct.Value;
        }
        set
        {
            if (_emulatedProduct == null || _emulatedProduct == SupportedDatabase.Unknown)
            {
                _emulatedProduct = value;
            }
        }
    }

    [AllowNull]
    public override string ConnectionString
    {
        get => _connectionString!;
        set => _connectionString = value;
    }

    public override int ConnectionTimeout => 0;
    public override string Database => _emulatedProduct?.ToString() ?? string.Empty;

    public override ConnectionState State => _state;

    private void ThrowConfiguredException(string defaultMessage)
    {
        throw _customFailureException ?? new InvalidOperationException(defaultMessage);
    }

    public int OpenCount { get; private set; }

    public int OpenAsyncCount { get; private set; }

    public override void Open()
    {
        if (_state == ConnectionState.Open)
        {
            return; // Already open, don't change state again
        }

        if (_isBroken)
        {
            throw new InvalidOperationException("Connection is broken");
        }

        _openCallCount++;

        // Check if we should use shared factory counter
        if (_sharedFactory != null && _sharedFailAfterOpenCount.HasValue)
        {
            var sharedCount = _sharedFactory.IncrementSharedOpenCount();
            if (sharedCount > _sharedFailAfterOpenCount.Value)
            {
                var original = _state;
                _state = ConnectionState.Broken;
                _isBroken = true;
                RaiseStateChangedEvent(original);
                ThrowConfiguredException("Connection failed after " + _sharedFailAfterOpenCount.Value + " opens");
            }
        }
        // Check if we should fail after a specific number of opens (per connection)
        else if (_failAfterOpenCount.HasValue && _openCallCount > _failAfterOpenCount.Value)
        {
            var original = _state;
            _state = ConnectionState.Broken;
            _isBroken = true;
            RaiseStateChangedEvent(original);
            ThrowConfiguredException("Connection failed after " + _failAfterOpenCount.Value + " opens");
        }

        // Check if we should fail on open
        if (_shouldFailOnOpen)
        {
            if (_factoryRef?.ShouldSkipThisOpen() == true)
            {
                // Skip this open (factory-level first open)
            }
            else
            {
                ThrowConfiguredException("Simulated connection open failure");
            }
        }

        // Check if connection should be broken (factory decides)
        if (_isBroken)
        {
            if (_factoryRef?.ShouldSkipThisOpen() == true)
            {
                // Allow this open, but mark as broken for future opens
                _isBroken = false; // Temporarily allow this open
                var original = _state;
                _state = ConnectionState.Open;
                RaiseStateChangedEvent(original);
                return; // Exit early, don't do normal open logic
            }

            throw new InvalidOperationException("Connection is broken");
        }

        OpenCount++;
        ParseEmulatedProduct(ConnectionString);
        var originalState = _state;

        _state = ConnectionState.Open;
        RaiseStateChangedEvent(originalState);
    }

    public override void Close()
    {
        if (_closeFailureException != null)
        {
            throw _closeFailureException;
        }
        var original = _state;
        _state = ConnectionState.Closed;
        RaiseStateChangedEvent(original);
    }

    public override void ChangeDatabase(string databaseName)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        Close();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await CloseAsync();
        await base.DisposeAsync();
    }

    public override Task CloseAsync()
    {
        Close();
        return Task.CompletedTask;
    }

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        OpenAsyncCount++;

        try
        {
            Open();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    private SupportedDatabase ParseEmulatedProduct(string connStr)
    {
        if (EmulatedProduct == SupportedDatabase.Unknown)
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connStr };
            if (!builder.TryGetValue("EmulatedProduct", out var raw))
            {
                EmulatedProduct = SupportedDatabase.Unknown;
            }
            else
            {
                // If parsing fails, default to Unknown rather than throwing
                var rawText = raw?.ToString();
                EmulatedProduct = Enum.TryParse<SupportedDatabase>(rawText, true, out var result)
                    ? result
                    : SupportedDatabase.Unknown;
            }
        }

        return EmulatedProduct;
    }

    private void RaiseStateChangedEvent(ConnectionState originalState)
    {
        if (_state != originalState)
        {
            OnStateChange(new StateChangeEventArgs(originalState, _state));
        }
    }

    /// <summary>
    /// Configure the connection to throw an exception on Close/Dispose.
    /// </summary>
    public void SetFailOnClose(Exception? exception)
    {
        _closeFailureException = exception;
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        if (_shouldFailOnBeginTransaction)
        {
            ThrowConfiguredException("Simulated transaction begin failure");
        }

        if (_isBroken)
        {
            throw new InvalidOperationException("Cannot begin transaction on broken connection");
        }

        if (_state != ConnectionState.Open)
        {
            throw new InvalidOperationException("Connection must be open to begin transaction");
        }

        return new fakeDbTransaction(this, isolationLevel);
    }

    protected override DbCommand CreateDbCommand()
    {
        if (_shouldFailOnCommand)
        {
            ThrowConfiguredException("Simulated command creation failure");
        }

        if (_isBroken)
        {
            throw new InvalidOperationException("Cannot create command on broken connection");
        }

        return new fakeDbCommand(this);
    }

    public override DataTable GetSchema()
    {
        if (_schemaTable != null)
        {
            return _schemaTable;
        }

        if (_emulatedProduct is null)
        {
            throw new InvalidOperationException("EmulatedProduct must be configured via connection string.");
        }

        if (_emulatedProduct == SupportedDatabase.Unknown)
        {
            _schemaTable = new DataTable();
            _schemaTable.Columns.Add("DataSourceProductName", typeof(string));
            _schemaTable.Columns.Add("DataSourceProductVersion", typeof(string));
            _schemaTable.Columns.Add("ParameterMarkerPattern", typeof(string));
            _schemaTable.Columns.Add("ParameterMarkerFormat", typeof(string));
            _schemaTable.Columns.Add("ParameterNameMaxLength", typeof(int));
            _schemaTable.Columns.Add("ParameterNamePattern", typeof(string));
            _schemaTable.Columns.Add("ParameterNamePatternRegex", typeof(string));
            _schemaTable.Columns.Add("SupportsNamedParameters", typeof(bool));
            _schemaTable.Rows.Add("UnknownDb", "1", "@p[0-9]+", "@{0}", 64, "@\\w+", "@\\w+", true);
            return _schemaTable;
        }

        var resourceName = $"pengdows.crud.fakeDb.xml.{_emulatedProduct}.schema.xml";

        using var stream = typeof(fakeDbConnection).Assembly
                               .GetManifestResourceStream(resourceName)
                           ?? throw new FileNotFoundException($"Embedded schema not found: {resourceName}");

        var table = new DataTable();
        table.ReadXml(stream);
        _schemaTable = table;
        return _schemaTable;
    }

    public override DataTable GetSchema(string meta)
    {
        if (_schemaTable != null)
        {
            return _schemaTable;
        }

        if (_emulatedProduct is null)
        {
            throw new InvalidOperationException("EmulatedProduct must be configured via connection string.");
        }

        if (_emulatedProduct == SupportedDatabase.Unknown)
        {
            _schemaTable = new DataTable();
            _schemaTable.Columns.Add("DataSourceProductName", typeof(string));
            _schemaTable.Columns.Add("DataSourceProductVersion", typeof(string));
            _schemaTable.Columns.Add("ParameterMarkerPattern", typeof(string));
            _schemaTable.Columns.Add("ParameterMarkerFormat", typeof(string));
            _schemaTable.Columns.Add("ParameterNameMaxLength", typeof(int));
            _schemaTable.Columns.Add("ParameterNamePattern", typeof(string));
            _schemaTable.Columns.Add("ParameterNamePatternRegex", typeof(string));
            _schemaTable.Columns.Add("SupportsNamedParameters", typeof(bool));
            _schemaTable.Rows.Add("UnknownDb", "1", "@p[0-9]+", "@{0}", 64, "@\\w+", "@\\w+", true);
            return _schemaTable;
        }

        var resourceName = $"pengdows.crud.fakeDb.xml.{_emulatedProduct}.schema.xml";

        using var stream = typeof(fakeDbConnection).Assembly
                               .GetManifestResourceStream(resourceName)
                           ?? throw new FileNotFoundException($"Embedded schema not found: {resourceName}");

        var table = new DataTable();
        table.ReadXml(stream);
        _schemaTable = table;
        return _schemaTable;
    }
}
