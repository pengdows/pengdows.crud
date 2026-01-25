namespace pengdows.crud.@internal;

internal sealed class ClauseCounters
{
    private int _set;
    private int _where;
    private int _join;
    private int _key;
    private int _ver;
    private int _ins;

    public string NextSet()
    {
        return $"s{_set++}";
    }

    public string NextWhere()
    {
        return $"w{_where++}";
    }

    public string NextJoin()
    {
        return $"j{_join++}";
    }

    public string NextKey()
    {
        return $"k{_key++}";
    }

    public string NextVer()
    {
        return $"v{_ver++}";
    }

    public string NextIns()
    {
        return $"i{_ins++}";
    }
}