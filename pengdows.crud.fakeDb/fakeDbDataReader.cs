#region

using System.Collections;
using System.Data;
using System.Data.Common;

#endregion

namespace pengdows.crud.fakeDb;

public class fakeDbDataReader : DbDataReader
{
    private readonly List<Dictionary<string, object>> _rows;
    private readonly string[] _columnNames;
    private int _index = -1;

    public fakeDbDataReader(
        IEnumerable<Dictionary<string, object>>? rows = null)
    {
        _rows = rows?.ToList() ?? new List<Dictionary<string, object>>();
        // Establish consistent column ordering from the first row
        _columnNames = _rows.Count > 0 ? _rows[0].Keys.ToArray() : Array.Empty<string>();
    }

    public fakeDbDataReader() : this(new List<Dictionary<string, object>>())
    {
    }

    public override int FieldCount
        => _columnNames.Length;

    public override bool HasRows
        => _rows.Count > 0;

    private bool _isClosed = false;
    
    // Stubs for unused members
    public override int Depth => 0;
    public override int RecordsAffected => 0;

    public override object this[int i] => GetValue(i);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool IsClosed => _isClosed;

    public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
    {
        var value = GetValue(ordinal);
        return Task.FromResult((T)Convert.ChangeType(value, typeof(T)));
    }

    public override bool Read()
    {
        return ++_index < _rows.Count;
    }

    public override Task<bool> ReadAsync(CancellationToken _)
    {
        return Task.FromResult(Read());
    }

    public override object GetValue(int i)
    {
        var columnName = _columnNames[i];
        return _rows[_index][columnName];
    }

    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }
        return count;
    }

    public override string GetName(int i)
    {
        return _columnNames[i];
    }

    public override int GetOrdinal(string name)
    {
        return Array.IndexOf(_columnNames, name);
    }

    public override bool IsDBNull(int i)
    {
        return GetValue(i) is null || GetValue(i) == DBNull.Value;
    }

    public override bool NextResult()
    {
        return false;
    }

    public override bool GetBoolean(int i)
    {
        return (bool)GetValue(i);
    }

    public override byte GetByte(int i)
    {
        return (byte)GetValue(i);
    }

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        throw new NotSupportedException();
    }

    public override char GetChar(int i)
    {
        return (char)GetValue(i);
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var data = (string)GetValue(ordinal);
        var copyLength = Math.Min(length, data.Length - dataOffset);
        if (buffer != null && copyLength > 0)
        {
            data.CopyTo((int)dataOffset, buffer, bufferOffset, (int)copyLength);
        }
        return copyLength;
    }

    public override string GetDataTypeName(int i)
    {
        return GetValue(i).GetType().Name;
    }

    public override DateTime GetDateTime(int i)
    {
        return (DateTime)GetValue(i);
    }

    public override decimal GetDecimal(int i)
    {
        return (decimal)GetValue(i);
    }

    public override double GetDouble(int i)
    {
        return (double)GetValue(i);
    }

    public override Type GetFieldType(int ordinal)
    {
        return GetValue(ordinal).GetType();
    }

    public override float GetFloat(int i)
    {
        return (float)GetValue(i);
    }

    public override Guid GetGuid(int i)
    {
        return (Guid)GetValue(i);
    }

    public override short GetInt16(int i)
    {
        return (short)GetValue(i);
    }

    public override int GetInt32(int i)
    {
        return (int)GetValue(i);
    }

    public override long GetInt64(int i)
    {
        return (long)GetValue(i);
    }

    public override string GetString(int i)
    {
        return (string)GetValue(i);
    }

    public override DataTable? GetSchemaTable()
    {
        return null;
    }

    // Remaining members can throw or return defaults
    public override IEnumerator GetEnumerator()
    {
        return _rows.GetEnumerator();
    }

    public override void Close()
    {
        _isClosed = true;
    }

    protected override DbDataReader GetDbDataReader(int ordinal)
    {
        // Return a new reader with the nested data - this is rarely used in practice
        // Most databases don't support hierarchical data in GetData()
        
        // Check if we have a valid row position before trying to access data
        if (_index >= 0 && _index < _rows.Count)
        {
            var nestedValue = GetValue(ordinal);
            if (nestedValue is IEnumerable<Dictionary<string, object>> nestedRows)
            {
                return new fakeDbDataReader(nestedRows);
            }
        }
        
        // For non-nested data or invalid position, return an empty reader
        return new fakeDbDataReader(new List<Dictionary<string, object>>());
    }
}
