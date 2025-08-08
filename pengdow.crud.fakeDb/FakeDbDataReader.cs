#region

using System.Collections;
using System.Data;
using System.Data.Common;

#endregion

namespace pengdow.crud.FakeDb;

public class FakeDbDataReader : DbDataReader
{
    private readonly List<Dictionary<string, object>> _rows;
    private int _index = -1;

    public FakeDbDataReader(
        IEnumerable<Dictionary<string, object>> rows = null)
    {
        _rows = rows?.ToList() ?? new List<Dictionary<string, object>>();
    }

    public FakeDbDataReader() : this(new List<Dictionary<string, object>>())
    {
    }

    public override int FieldCount
        => _rows.FirstOrDefault()?.Count ?? 0;

    public override bool HasRows
        => _rows.Count > 0;

    // Stubs for unused members
    public override int Depth => 0;
    public override int RecordsAffected => 0;

    public override object this[int i] => GetValue(i);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool IsClosed => false;

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
        return _rows[_index].Values.ElementAt(i);
    }

    public override int GetValues(object[] values)
    {
        throw new NotImplementedException();
    }

    public override string GetName(int i)
    {
        return _rows[Math.Max(_index, 0)].Keys.ElementAt(i);
    }

    public override int GetOrdinal(string name)
    {
        return _rows[Math.Max(_index, 0)].Keys.ToList().IndexOf(name);
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

    public override long GetBytes(int i, long o, byte[] b, int bi, int l)
    {
        throw new NotSupportedException();
    }

    public override char GetChar(int i)
    {
        return (char)GetValue(i);
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        throw new NotImplementedException();
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
        throw new NotImplementedException();
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
    }
}