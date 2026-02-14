// =============================================================================
// FILE: ClauseCounters.cs
// PURPOSE: Generates unique parameter names for SQL clause components.
//
// AI SUMMARY:
// - Generates sequential, unique parameter name prefixes for SQL building.
// - Each clause type has its own counter to avoid collisions.
// - Methods:
//   * NextSet() → "s0", "s1", "s2"... (SET clause parameters)
//   * NextWhere() → "w0", "w1", "w2"... (WHERE clause parameters)
//   * NextJoin() → "j0", "j1", "j2"... (JOIN parameters)
//   * NextKey() → "k0", "k1", "k2"... (key/ID parameters)
//   * NextVer() → "v0", "v1", "v2"... (version parameters)
//   * NextIns() → "i0", "i1", "i2"... (INSERT parameters)
//   * NextBatch() → "b0", "b1", "b2"... (batch INSERT/UPSERT parameters)
// - Used by TableGateway and SQL builders for unique parameter naming.
// - Instance per operation; not shared across contexts.
// =============================================================================

namespace pengdows.crud.@internal;

internal sealed class ClauseCounters
{
    private int _set;
    private int _where;
    private int _join;
    private int _key;
    private int _ver;
    private int _ins;
    private int _batch;

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

    public string NextBatch()
    {
        return $"b{_batch++}";
    }
}