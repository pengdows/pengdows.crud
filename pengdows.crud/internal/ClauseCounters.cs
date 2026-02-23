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
// - Static pre-built caches eliminate string interpolation allocations for
//   all typical entity sizes (fallback to interpolation beyond cache size).
// =============================================================================

namespace pengdows.crud.@internal;

internal struct ClauseCounters
{
    // Pre-built name caches: sizes cover all realistic entities.
    // >64 columns is exceptional; >256 batch params falls back to interpolation.
    private static readonly string[] SetCache = Build("s", 64);
    private static readonly string[] WhereCache = Build("w", 64);
    private static readonly string[] JoinCache = Build("j", 16);
    private static readonly string[] KeyCache = Build("k", 16);
    private static readonly string[] VerCache = Build("v", 8);
    private static readonly string[] InsCache = Build("i", 64);
    private static readonly string[] BatchCache = Build("b", 256);

    private static string[] Build(string prefix, int count)
    {
        var names = new string[count];
        for (var i = 0; i < count; i++)
        {
            names[i] = prefix + i;
        }

        return names;
    }

    private int _set;
    private int _where;
    private int _join;
    private int _key;
    private int _ver;
    private int _ins;
    private int _batch;

    public string NextSet() => _set < SetCache.Length ? SetCache[_set++] : $"s{_set++}";
    public string NextWhere() => _where < WhereCache.Length ? WhereCache[_where++] : $"w{_where++}";
    public string NextJoin() => _join < JoinCache.Length ? JoinCache[_join++] : $"j{_join++}";
    public string NextKey() => _key < KeyCache.Length ? KeyCache[_key++] : $"k{_key++}";
    public string NextVer() => _ver < VerCache.Length ? VerCache[_ver++] : $"v{_ver++}";
    public string NextIns() => _ins < InsCache.Length ? InsCache[_ins++] : $"i{_ins++}";
    public string NextBatch() => _batch < BatchCache.Length ? BatchCache[_batch++] : $"b{_batch++}";
}