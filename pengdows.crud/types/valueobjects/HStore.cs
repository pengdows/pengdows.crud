using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using pengdows.crud.@internal;

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// Represents PostgreSQL HSTORE data type - a key-value store within a single column.
/// Optimized for database round-trips with proper escaping.
/// </summary>
public readonly struct HStore : IEquatable<HStore>, IEnumerable<KeyValuePair<string, string?>>
{
    private readonly Dictionary<string, string?>? _data;

    public HStore(Dictionary<string, string?> data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public HStore(IEnumerable<KeyValuePair<string, string?>> pairs)
    {
        _data = new Dictionary<string, string?>();
        foreach (var pair in pairs)
        {
            _data[pair.Key] = pair.Value;
        }
    }

    /// <summary>
    /// Get value by key. Returns null if key doesn't exist.
    /// </summary>
    public string? this[string key]
    {
        get => _data?.TryGetValue(key, out var value) == true ? value : null;
    }

    /// <summary>
    /// Check if the HStore contains a key.
    /// </summary>
    public bool ContainsKey(string key) => _data?.ContainsKey(key) == true;

    /// <summary>
    /// Get all keys in the HStore.
    /// </summary>
    public IEnumerable<string> Keys => _data?.Keys ?? Enumerable.Empty<string>();

    /// <summary>
    /// Get all values in the HStore (including nulls).
    /// </summary>
    public IEnumerable<string?> Values => _data?.Values ?? Enumerable.Empty<string?>();

    /// <summary>
    /// Number of key-value pairs.
    /// </summary>
    public int Count => _data?.Count ?? 0;

    /// <summary>
    /// True if the HStore is empty.
    /// </summary>
    public bool IsEmpty => Count == 0;

    /// <summary>
    /// Parse HSTORE string format like 'key1=>val1, key2=>val2, key3=>NULL'.
    /// </summary>
    public static HStore Parse(string hstoreText)
    {
        if (string.IsNullOrWhiteSpace(hstoreText))
            return new HStore(new Dictionary<string, string?>());

        var data = new Dictionary<string, string?>();
        var pairs = SplitHStorePairs(hstoreText);

        foreach (var pair in pairs)
        {
            var arrowIndex = pair.IndexOf("=>");
            if (arrowIndex == -1)
                throw new FormatException($"Invalid HSTORE pair format: {pair}");

            var key = UnescapeHStoreValue(pair[..arrowIndex].Trim());
            var valueText = pair[(arrowIndex + 2)..].Trim();

            string? value = null;
            if (!string.Equals(valueText, "NULL", StringComparison.OrdinalIgnoreCase))
            {
                value = UnescapeHStoreValue(valueText);
            }

            data[key] = value;
        }

        return new HStore(data);
    }

    /// <summary>
    /// Convert to canonical HSTORE string format.
    /// </summary>
    public override string ToString()
    {
        if (_data == null || _data.Count == 0)
            return "";

        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        bool first = true;

        foreach (var pair in _data)
        {
            if (!first) sb.Append(", ");
            first = false;

            sb.Append(EscapeHStoreValue(pair.Key));
            sb.Append("=>");

            if (pair.Value == null)
            {
                sb.Append("NULL");
            }
            else
            {
                sb.Append(EscapeHStoreValue(pair.Value));
            }
        }

        return sb.ToString();
    }

    private static IEnumerable<string> SplitHStorePairs(string hstore)
    {
        var pairs = new List<string>();
        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        bool inQuotes = false;
        bool escaped = false;

        for (int i = 0; i < hstore.Length; i++)
        {
            char c = hstore[i];

            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                sb.Append(c);
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c);
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                pairs.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        if (sb.Length > 0)
        {
            pairs.Add(sb.ToString().Trim());
        }

        return pairs;
    }

    private static string EscapeHStoreValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        // Check if quoting is needed
        bool needsQuoting = value.Any(c => char.IsWhiteSpace(c) || c == '"' || c == '\\' || c == ',' || c == '=' || c == '>');

        if (!needsQuoting)
            return value;

        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        sb.Append('"');

        foreach (char c in value)
        {
            if (c == '"' || c == '\\')
                sb.Append('\\');
            sb.Append(c);
        }

        sb.Append('"');
        return sb.ToString();
    }

    private static string UnescapeHStoreValue(string escapedValue)
    {
        if (string.IsNullOrEmpty(escapedValue))
            return "";

        escapedValue = escapedValue.Trim();

        if (escapedValue.Length >= 2 && escapedValue[0] == '"' && escapedValue[^1] == '"')
        {
            escapedValue = escapedValue[1..^1];
        }

        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        bool escaped = false;

        foreach (char c in escapedValue)
        {
            if (escaped)
            {
                sb.Append(c);
                escaped = false;
            }
            else if (c == '\\')
            {
                escaped = true;
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    public IEnumerator<KeyValuePair<string, string?>> GetEnumerator()
    {
        return (_data ?? new Dictionary<string, string?>()).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(HStore other)
    {
        if (_data == null && other._data == null) return true;
        if (_data == null || other._data == null) return false;
        if (_data.Count != other._data.Count) return false;

        foreach (var pair in _data)
        {
            if (!other._data.TryGetValue(pair.Key, out var otherValue) ||
                !string.Equals(pair.Value, otherValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is HStore other && Equals(other);

    public override int GetHashCode()
    {
        if (_data == null) return 0;

        int hash = 0;
        foreach (var pair in _data)
        {
            hash ^= HashCode.Combine(pair.Key, pair.Value);
        }
        return hash;
    }

    public static bool operator ==(HStore left, HStore right) => left.Equals(right);
    public static bool operator !=(HStore left, HStore right) => !left.Equals(right);
}
