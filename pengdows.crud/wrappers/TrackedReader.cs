#region

using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.infrastructure;

#endregion

namespace pengdows.crud.wrappers;

public class TrackedReader : SafeAsyncDisposableBase, ITrackedReader
{
    private readonly ITrackedConnection _connection;
    private readonly IAsyncDisposable _connectionLocker;
    private readonly DbDataReader _reader;
    private readonly bool _shouldCloseConnection;
    private readonly DbCommand? _command;

    public TrackedReader(DbDataReader reader,
        ITrackedConnection connection,
        IAsyncDisposable connectionLocker,
        bool shouldCloseConnection,
        DbCommand? command = null)
    {
        _reader = reader;
        _connection = connection;
        _connectionLocker = connectionLocker;
        _shouldCloseConnection = shouldCloseConnection;
        _command = command;
    }

    protected override void DisposeManaged()
    {
        _command?.Dispose();
        _reader.Dispose();
        if (_shouldCloseConnection)
        {
            _connection.Close();
        }

        DisposeLockerSynchronously();
    }

    public bool Read()
    {
        if (_reader.Read())
        {
            return true;
        }

        try
        {
            Dispose();
        }
        catch
        {
            //ignore the error
        }

        return false;
    }


    public bool GetBoolean(int i)
    {
        return _reader.GetBoolean(i);
    }

    public byte GetByte(int i)
    {
        return _reader.GetByte(i);
    }

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length)
    {
        return _reader.GetBytes(i, fieldOffset, buffer, bufferoffset, length);
    }

    public char GetChar(int i)
    {
        return _reader.GetChar(i);
    }

    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length)
    {
        return _reader.GetChars(i, fieldoffset, buffer, bufferoffset, length);
    }

    public IDataReader GetData(int i)
    {
        return _reader.GetData(i);
    }

    public string GetDataTypeName(int i)
    {
        return _reader.GetDataTypeName(i);
    }

    public decimal GetDecimal(int i)
    {
        return _reader.GetDecimal(i);
    }

    public double GetDouble(int i)
    {
        return _reader.GetDouble(i);
    }

    public float GetFloat(int i)
    {
        return _reader.GetFloat(i);
    }

    public short GetInt16(int i)
    {
        return _reader.GetInt16(i);
    }

    public int GetInt32(int i)
    {
        return _reader.GetInt32(i);
    }

    public long GetInt64(int i)
    {
        return _reader.GetInt64(i);
    }

    public string GetName(int i)
    {
        return _reader.GetName(i);
    }

    public int GetOrdinal(string name)
    {
        return _reader.GetOrdinal(name);
    }

    public string GetString(int i)
    {
        return _reader.GetString(i);
    }

    public object GetValue(int i)
    {
        return _reader.GetValue(i);
    }

    public int GetValues(object[] values)
    {
        return _reader.GetValues(values);
    }

    public bool IsDBNull(int i)
    {
        return _reader.IsDBNull(i);
    }

    public int FieldCount => _reader.FieldCount;
    public object this[int i] => _reader[i];
    public object this[string name] => _reader[name];

    public void Close()
    {
        _reader.Close();
    }

    public DataTable? GetSchemaTable()
    {
        return _reader.GetSchemaTable();
    }

    public bool NextResult()
    {
        // Multiple result sets are not supported by policy.
        throw new NotSupportedException("Multiple result sets are not supported.");
    }

    public int Depth => _reader.Depth;
    public bool IsClosed => _reader.IsClosed;
    public int RecordsAffected => _reader.RecordsAffected;

    protected override async ValueTask DisposeManagedAsync()
    {
        if (_command is IAsyncDisposable asyncCmd)
        {
            await asyncCmd.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _command?.Dispose();
        }

        await _reader.DisposeAsync().ConfigureAwait(false);
        if (_shouldCloseConnection)
        {
            _connection.Close();
        }

        await _connectionLocker.DisposeAsync().ConfigureAwait(false);
    }

    public async Task<bool> ReadAsync()
    {
        if (await _reader.ReadAsync().ConfigureAwait(false))
        {
            return true;
        }

        await DisposeAsync().ConfigureAwait(false); // Auto-dispose when done reading
        return false;
    }

    public DateTime GetDateTime(int i)
    {
        return _reader.GetDateTime(i);
    }

    public Type GetFieldType(int i)
    {
        return _reader.GetFieldType(i);
    }

    public Guid GetGuid(int i)
    {
        return _reader.GetGuid(i);
    }

    private void DisposeLockerSynchronously()
    {
        if (_connectionLocker == null)
        {
            return;
        }

        if (_connectionLocker is IDisposable disposable)
        {
            disposable.Dispose();
            return;
        }

        Task.Run(async () =>
        {
            await _connectionLocker.DisposeAsync().ConfigureAwait(false);
        }).GetAwaiter().GetResult();
    }
}
