// =============================================================================
// FILE: RecordsetShape.cs
// PURPOSE: Structural identity of a DbDataReader's column shape, for use as a plan-cache key.
//
// AI SUMMARY:
// - Immutable, structurally-equatable (field names + types) shape descriptor.
// - Shared by BaseTableGateway.Reader.cs and DataReaderMapper so both plan caches key by
//   structural equality instead of a bare hash (a hash collision would otherwise silently
//   reuse the wrong compiled plan; BP-102, from 3.0 CORE-013).
// - Two callers: BaseTableGateway's hot path (rents transient arrays, only Persist()s on a
//   cache miss) and DataReaderMapper's cold path (builds an owned array directly, no pooling).
// =============================================================================

namespace pengdows.crud.@internal;

/// <summary>
/// Structural shape of a recordset's columns (names + types), used as a cache key so a hash
/// collision between two different shapes can never silently reuse the wrong compiled plan.
/// </summary>
internal readonly struct RecordsetShape : IEquatable<RecordsetShape>
{
    private readonly string[] _names;
    private readonly Type[] _types;
    private readonly int _fieldCount;

    // Lookup-only constructor: wraps caller-owned (possibly pooled/oversized/rented) arrays.
    // Never persisted as a dictionary key unless the caller already owns the arrays outright,
    // or calls Persist() — see Persist().
    public RecordsetShape(string[] names, Type[] types, int fieldCount)
    {
        _names = names;
        _types = types;
        _fieldCount = fieldCount;
    }

    /// <summary>
    /// Returns a copy safe to store as a cache key — for a caller whose backing arrays are
    /// rented/pooled and returned (and possibly reused) after this call returns.
    /// </summary>
    public RecordsetShape Persist()
    {
        var names = new string[_fieldCount];
        var types = new Type[_fieldCount];
        Array.Copy(_names, names, _fieldCount);
        Array.Copy(_types, types, _fieldCount);
        return new RecordsetShape(names, types, _fieldCount);
    }

    public bool Equals(RecordsetShape other)
    {
        if (_fieldCount != other._fieldCount)
        {
            return false;
        }

        for (var i = 0; i < _fieldCount; i++)
        {
            if (_types[i] != other._types[i])
            {
                return false;
            }

            if (!string.Equals(_names[i], other._names[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is RecordsetShape other && Equals(other);

    public override int GetHashCode()
    {
        var hashBuilder = new HashCode();
        hashBuilder.Add(_fieldCount);
        for (var i = 0; i < _fieldCount; i++)
        {
            hashBuilder.Add(_names[i], StringComparer.OrdinalIgnoreCase);
            hashBuilder.Add(_types[i]);
        }

        return hashBuilder.ToHashCode();
    }
}
