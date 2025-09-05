namespace pengdows.crud.@internal;

internal sealed class ClauseCounters
{
    private int _set;
    private int _where;
    private int _join;
    private int _key;
    private int _ver;
    private int _ins;

    public string NextSet() => $"s{_set++}";
    public string NextWhere() => $"w{_where++}";
    public string NextJoin() => $"j{_join++}";
    public string NextKey() => $"k{_key++}";
    public string NextVer() => $"v{_ver++}";
    public string NextIns() => $"i{_ins++}";
}
