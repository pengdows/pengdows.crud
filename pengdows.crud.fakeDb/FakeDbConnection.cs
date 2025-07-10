#region

using System;
using System.Data;
using System.Data.Common;
using System.IO;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.FakeDb;

public class FakeDbConnection : DbConnection, IDbConnection, IDisposable, IAsyncDisposable
{
    private string _connectionString = string.Empty;
    private SupportedDatabase? _emulatedProduct;
    private DataTable? _schemaTable;
    private ConnectionState _state = ConnectionState.Closed;
    public override string DataSource => "FakeSource";
    public override string ServerVersion => "1.0";

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
                _emulatedProduct = value;
        }
    }

    public override string ConnectionString
    {
        get => _connectionString;
        set => _connectionString = value;
    }

    public int ConnectionTimeout { get; }
    public override string Database => _emulatedProduct?.ToString() ?? string.Empty;

    public override ConnectionState State => _state;

    public override void Open()
    {
        ParseEmulatedProduct(ConnectionString);
        var original = _state;

        _state = ConnectionState.Open;
        RaiseStateChangedEvent(original);
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

    public override async Task CloseAsync()
    {
        Close();
    }

    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        Open();
    }

    private SupportedDatabase ParseEmulatedProduct(string connStr)
    {
        if (EmulatedProduct == SupportedDatabase.Unknown)
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connStr };
            if (!builder.TryGetValue("EmulatedProduct", out var raw))
                EmulatedProduct = SupportedDatabase.Unknown;
            else
                EmulatedProduct = Enum.TryParse<SupportedDatabase>(raw.ToString(), true, out var result)
                    ? result
                    : throw new ArgumentException($"Invalid EmulatedProduct: {raw}");
        }

        return EmulatedProduct;
    }

    private void RaiseStateChangedEvent(ConnectionState originalState)
    {
        if (_state != originalState) OnStateChange(new StateChangeEventArgs(originalState, _state));
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        return new FakeDbTransaction(this, isolationLevel);
    }

    protected override DbCommand CreateDbCommand()
    {
        return new FakeDbCommand(this);
    }

    public override DataTable GetSchema()
    {
        if (_schemaTable != null) return _schemaTable;

        if (_emulatedProduct is null or SupportedDatabase.Unknown)
            throw new InvalidOperationException("EmulatedProduct must be configured via connection string.");

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
        if (_schemaTable != null) return _schemaTable;

        if (_emulatedProduct is null or SupportedDatabase.Unknown)
            throw new InvalidOperationException("EmulatedProduct must be configured via connection string.");

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