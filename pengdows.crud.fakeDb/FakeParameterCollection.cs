#region

using System.Collections;
using System.Data.Common;

#endregion

namespace pengdows.crud.FakeDb;

public class FakeParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _params = new();

    public override int Count => _params.Count;
    public override object SyncRoot => new();

    public new DbParameter this[int index]
    {
        get => _params[index];
        set => _params[index] = value;
    }

    public new DbParameter this[string parameterName] => _params.First(p => p.ParameterName == parameterName);

    protected override void SetParameter(string parameterName, DbParameter value)
    {
        throw new NotImplementedException();
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
        return _params.Any(p => p.ParameterName == value);
    }

    public override void RemoveAt(int index)
    {
        _params.RemoveAt(index);
    }

    public override void RemoveAt(string parameterName)
    {
        _params.RemoveAll(p => p.ParameterName == parameterName);
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
        var list = _params.Where(p => p.ParameterName == parameterName).ToList();
        if (list.Count < 1) throw new IndexOutOfRangeException(parameterName);

        return list[0];
    }

    public override int IndexOf(string parameterName)
    {
        return _params.FindIndex(p => p.ParameterName == parameterName);
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