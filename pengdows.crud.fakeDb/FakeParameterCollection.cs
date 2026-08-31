#region

using System.Collections;
using System.Data.Common;

#endregion

namespace pengdows.crud.fakeDb;

public class FakeParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _params = new();
    private readonly object _syncRoot = new();

    public override int Count => _params.Count;
    public override object SyncRoot => _syncRoot;

    public new DbParameter this[int index]
    {
        get => _params[index];
        set => _params[index] = value;
    }

    // Case-insensitive by parameter name, matching real ADO.NET provider parameter collections
    // (e.g. SqlParameterCollection), which resolve names case-insensitively.
    public new DbParameter this[string parameterName] =>
        _params.First(p => string.Equals(p.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));

    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var index = _params.FindIndex(p => string.Equals(p.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new IndexOutOfRangeException(parameterName);
        }

        _params[index] = value;
    }

    public override int Add(object value)
    {
        _params.Add((DbParameter)value);
        return _params.Count - 1;
    }

    public override void Clear()
    {
        _params.Clear();
    }

    public override bool Contains(string value)
    {
        return _params.Any(p => string.Equals(p.ParameterName, value, StringComparison.OrdinalIgnoreCase));
    }

    public override void RemoveAt(int index)
    {
        _params.RemoveAt(index);
    }

    public override void RemoveAt(string parameterName)
    {
        _params.RemoveAll(p => string.Equals(p.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));
    }

    protected override void SetParameter(int index, DbParameter value)
    {
        _params[index] = value;
    }

    public override IEnumerator GetEnumerator()
    {
        return _params.GetEnumerator();
    }

    protected override DbParameter GetParameter(int index)
    {
        return _params[index];
    }

    protected override DbParameter GetParameter(string parameterName)
    {
        var list = _params.Where(p => string.Equals(p.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (list.Count < 1)
        {
            throw new IndexOutOfRangeException(parameterName);
        }

        return list[0];
    }

    public override int IndexOf(string parameterName)
    {
        return _params.FindIndex(p => string.Equals(p.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));
    }

    public override bool Contains(object value)
    {
        return _params.Contains((DbParameter)value);
    }

    public override int IndexOf(object value)
    {
        return _params.IndexOf((DbParameter)value);
    }

    public override void Insert(int index, object value)
    {
        _params.Insert(index, (DbParameter)value);
    }

    public override void Remove(object value)
    {
        _params.Remove((DbParameter)value);
    }

    public override void CopyTo(Array array, int index)
    {
        _params.ToArray().CopyTo(array, index);
    }

    public override void AddRange(Array values)
    {
        _params.AddRange(values.Cast<DbParameter>());
    }
}