#region

using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;

#endregion

namespace pengdows.crud.fakeDb;

public class fakeDbDataReader : DbDataReader
{
    private readonly List<List<Dictionary<string, object>>> _resultSets;
    private int _currentSetIndex = 0;
    private int _index = -1;

    public fakeDbDataReader(
        IEnumerable<Dictionary<string, object>>? rows = null)
    {
        var single = rows?.ToList() ?? new List<Dictionary<string, object>>();
        _resultSets = new List<List<Dictionary<string, object>>> { single };
    }

    public fakeDbDataReader() : this((IEnumerable<Dictionary<string, object>>?)null)
    {
    }

    /// <summary>
    /// Creates a reader with multiple result sets, allowing <see cref="NextResult"/> to
    /// advance to subsequent sets. Used to simulate compound batch queries
    /// (e.g. INSERT followed by SELECT LAST_INSERT_ID()).
    /// </summary>
    internal fakeDbDataReader(IEnumerable<IEnumerable<Dictionary<string, object>>> resultSets)
    {
        _resultSets = resultSets.Select(rs => rs.ToList()).ToList();
        if (_resultSets.Count == 0)
        {
            _resultSets.Add(new List<Dictionary<string, object>>());
        }
    }

    private List<Dictionary<string, object>> CurrentRows => _resultSets[_currentSetIndex];

    private Dictionary<string, object>? CurrentRow =>
        _index >= 0 && _index < CurrentRows.Count ? CurrentRows[_index] : null;

    /// <summary>Returns the rows in the first result set. Used by RemainingReaderResults.</summary>
    internal List<Dictionary<string, object>> FirstResultSet => _resultSets[0];

    private static string[] GetKeys(Dictionary<string, object> row)
    {
        return row.Keys.ToArray();
    }

    public override int FieldCount
        => CurrentRow?.Count
           ?? (CurrentRows.Count > 0 ? CurrentRows[0].Count : 0);

    public override bool HasRows
        => CurrentRows.Count > 0;

    private bool _isClosed;

    // Stubs for unused members
    public override int Depth => 0;
    public override int RecordsAffected => 0;

    public override object this[int i] => GetValue(i);

    public override object this[string name]
    {
        get
        {
            var row = CurrentRow ?? (CurrentRows.Count > 0 ? CurrentRows[0] : null);
            if (row is null)
            {
                throw new IndexOutOfRangeException("No current row.");
            }

            return row[name];
        }
    }

    public override bool IsClosed => _isClosed;

    public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
    {
        var value = GetValue(ordinal);
        return Task.FromResult((T)Convert.ChangeType(value, typeof(T)));
    }

    public override bool Read()
    {
        return ++_index < CurrentRows.Count;
    }

    public override Task<bool> ReadAsync(CancellationToken _)
    {
        return Task.FromResult(Read());
    }

    public override object GetValue(int i)
    {
        var row = CurrentRow ?? (CurrentRows.Count > 0 ? CurrentRows[0] : null)
            ?? throw new IndexOutOfRangeException("No data rows.");
        var keys = GetKeys(row);
        return row[keys[i]];
    }

    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    public override string GetName(int i)
    {
        var row = CurrentRow ?? (CurrentRows.Count > 0 ? CurrentRows[0] : null)
            ?? throw new IndexOutOfRangeException("No data rows.");
        var keys = GetKeys(row);
        return keys[i];
    }

    public override int GetOrdinal(string name)
    {
        var row = CurrentRow ?? (CurrentRows.Count > 0 ? CurrentRows[0] : null)
            ?? throw new IndexOutOfRangeException("No data rows.");
        var keys = GetKeys(row);
        return Array.IndexOf(keys, name);
    }

    public override bool IsDBNull(int i)
    {
        var value = GetValue(i);
        return value is null || value == DBNull.Value;
    }

    public override bool NextResult()
    {
        if (_currentSetIndex + 1 < _resultSets.Count)
        {
            _currentSetIndex++;
            _index = -1;
            return true;
        }

        return false;
    }

    public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
        => Task.FromResult(NextResult());

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
        var data = GetValue(ordinal);
        if (data is not byte[] bytes)
        {
            // If it's not a byte array, return 0 to indicate no bytes copied
            return 0;
        }

        var available = bytes.LongLength - dataOffset;
        if (available <= 0)
        {
            return 0;
        }

        // When buffer is null, return the total available length (standard .NET GetBytes convention).
        if (buffer == null)
        {
            return available;
        }

        var toCopy = (int)Math.Min(length, available);
        if (toCopy > 0)
        {
            Array.Copy(bytes, (int)dataOffset, buffer, bufferOffset, toCopy);
        }

        return toCopy;
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
        var value = GetValue(i);
        return value switch
        {
            DateTime dt => dt,
            // Real SQLite drivers parse TEXT datetime columns in GetDateTime() using RoundtripKind.
            // Match that behavior so fake readers backed by string values work the same way.
            string s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture)
        };
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
        return CurrentRows.GetEnumerator();
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
        if (_index >= 0 && _index < CurrentRows.Count)
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