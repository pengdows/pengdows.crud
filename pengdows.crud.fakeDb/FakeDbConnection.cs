#region

using System.Data;
using System.Data.Common;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.FakeDb;

public class FakeDbConnection : DbConnection, IDbConnection, IDisposable, IAsyncDisposable
{
    private string _connectionString = string.Empty;
    private SupportedDatabase? _emulatedProduct;
    private DataTable? _schemaTable;
    private ConnectionState _state = ConnectionState.Closed;
    private string _serverVersion = "1.0";
    private int? _maxParameterLimit;
    private bool _shouldFailOnOpen;
    private bool _shouldFailOnCommand;
    private bool _shouldFailOnBeginTransaction;
    private Exception? _customFailureException;
    private int _openCallCount;
    private int? _failAfterOpenCount;
    private bool _isBroken;
    public override string DataSource => "FakeSource";
    public override string ServerVersion => GetEmulatedServerVersion();

    internal readonly Queue<IEnumerable<Dictionary<string, object>>> ReaderResults = new();
    internal readonly Queue<object?> ScalarResults = new();
    public readonly Queue<int> NonQueryResults = new();
    internal readonly Dictionary<string, object?> ScalarResultsByCommand = new();

    public void EnqueueReaderResult(IEnumerable<Dictionary<string, object>> rows)
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
    /// Sets the connection to fail on the next Open() or OpenAsync() call
    /// </summary>
    public void SetFailOnOpen(bool shouldFail = true)
    {
        _shouldFailOnOpen = shouldFail;
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
    /// Simulates a broken connection by setting state to Broken
    /// </summary>
    public void BreakConnection()
    {
        var original = _state;
        _state = ConnectionState.Broken;
        _isBroken = true;
        RaiseStateChangedEvent(original);
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
        _openCallCount = 0;
        _isBroken = false;
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

    public override string ConnectionString
    {
        get => _connectionString;
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
        if (_isBroken)
        {
            throw new InvalidOperationException("Connection is broken");
        }

        _openCallCount++;
        
        // Check if we should fail after a specific number of opens
        if (_failAfterOpenCount.HasValue && _openCallCount > _failAfterOpenCount.Value)
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
            ThrowConfiguredException("Simulated connection open failure");
        }

        OpenCount++;
        ParseEmulatedProduct(ConnectionString);
        var originalState = _state;

        _state = ConnectionState.Open;
        RaiseStateChangedEvent(originalState);
    }

    public override void Close()
    {
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
        CloseAsync();
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
                EmulatedProduct = Enum.TryParse<SupportedDatabase>(raw.ToString(), true, out var result)
                    ? result
                    : throw new ArgumentException($"Invalid EmulatedProduct: {raw}");
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

        return new FakeDbTransaction(this, isolationLevel);
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

        return new FakeDbCommand(this);
    }

    public override DataTable GetSchema()
    {
        if (_schemaTable != null)
        {
            return _schemaTable;
        }

        if (_emulatedProduct is null or SupportedDatabase.Unknown)
        {
            throw new InvalidOperationException("EmulatedProduct must be configured via connection string.");
        }

        var resourceName = $"pengdows.crud.fakeDb.xml.{_emulatedProduct}.schema.xml";

        using var stream = typeof(FakeDbConnection).Assembly
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

        if (_emulatedProduct is null or SupportedDatabase.Unknown)
        {
            throw new InvalidOperationException("EmulatedProduct must be configured via connection string.");
        }

        var resourceName = $"pengdows.crud.fakeDb.xml.{_emulatedProduct}.schema.xml";

        using var stream = typeof(FakeDbConnection).Assembly
                               .GetManifestResourceStream(resourceName)
                           ?? throw new FileNotFoundException($"Embedded schema not found: {resourceName}");

        var table = new DataTable();
        table.ReadXml(stream);
        _schemaTable = table;
        return _schemaTable;
    }
}