// =============================================================================
// FILE: HierarchyId.cs
// PURPOSE: Immutable value object for SQL Server's hierarchyid type (TYPE-016).
//
// AI SUMMARY:
// - A path of levels, each one or more integers: "/", "/1/", "/1/2.5/-3/".
// - Parse()/TryParse()/ToString() use SQL Server's canonical text form.
// - FromSqlServerBytes()/ToSqlServerBytes() implement SQL Server's OrdPath binary
//   encoding, so the value is read without Microsoft.SqlServer.Types (SqlClient's
//   GetBytes returns these bytes when GetValue can't load that assembly).
// - CompareTo compares the encodings bitwise, which is SQL Server's own
//   (depth-first) ordering; the tests pin it to a live ORDER BY.
// - Written to the database as its text form; SQL Server converts nvarchar to
//   hierarchyid implicitly, and other databases store the path as text.
// - default(HierarchyId) is the root.
// =============================================================================

using System.Globalization;
using System.Text;

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// Immutable value object for a node in a hierarchy, with the semantics of SQL Server's
/// <c>hierarchyid</c>: a path such as <c>/1/</c>, <c>/1/2/</c> or <c>/1/2.5/</c>.
/// </summary>
/// <remarks>
/// Needs no <c>Microsoft.SqlServer.Types</c> reference: pengdows reads SQL Server's stored
/// encoding directly and writes the text form, which SQL Server converts implicitly. On other
/// databases the path is stored as text. <see cref="default"/> is <see cref="Root"/>.
/// </remarks>
public readonly struct HierarchyId : IEquatable<HierarchyId>, IComparable<HierarchyId>
{
    // Range SQL Server accepts for one component (confirmed live: one past either end is rejected).
    private const long MinComponent = -281479271682120;
    private const long MaxComponent = 281479271683151;

    private readonly long[][]? _levels;

    private HierarchyId(long[][] levels)
    {
        _levels = levels.Length == 0 ? null : levels;
    }

    /// <summary>The root node, <c>/</c>.</summary>
    public static HierarchyId Root => default;

    /// <summary>Depth of the node; the root is 0.</summary>
    public int Level => _levels?.Length ?? 0;

    /// <summary>
    /// Parses SQL Server's text form (<c>/</c>, <c>/1/</c>, <c>/1/2.5/-3/</c>).
    /// </summary>
    /// <exception cref="FormatException">The text isn't a hierarchyid SQL Server accepts.</exception>
    public static HierarchyId Parse(string text)
    {
        return TryParse(text, out var result)
            ? result
            : throw new FormatException($"'{text}' is not a valid hierarchyid path.");
    }

    public static bool TryParse(string? text, out HierarchyId result)
    {
        result = default;
        if (text is null || text.Length == 0 || text[0] != '/' || text[^1] != '/')
        {
            return false;
        }

        if (text.Length == 1)
        {
            return true;
        }

        var levelTexts = text.Substring(1, text.Length - 2).Split('/');
        var levels = new long[levelTexts.Length][];
        for (var i = 0; i < levelTexts.Length; i++)
        {
            var parts = levelTexts[i].Split('.');
            var components = new long[parts.Length];
            for (var j = 0; j < parts.Length; j++)
            {
                if (!TryParseComponent(parts[j], out components[j]))
                {
                    return false;
                }

                // A component followed by '.' is stored as value + 1 (see Encode).
                var last = j == parts.Length - 1;
                if (!last && components[j] == MaxComponent)
                {
                    return false;
                }
            }

            levels[i] = components;
        }

        result = new HierarchyId(levels);
        return true;
    }

    private static bool TryParseComponent(string text, out long value)
    {
        value = 0;
        var digits = text.StartsWith('-') ? text.AsSpan(1) : text.AsSpan();
        if (digits.Length == 0)
        {
            return false;
        }

        foreach (var c in digits)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
               && value is >= MinComponent and <= MaxComponent;
    }

    /// <summary>
    /// Returns the ancestor <paramref name="n"/> levels up; 0 is the node itself.
    /// </summary>
    public HierarchyId GetAncestor(int n)
    {
        if (n < 0 || n > Level)
        {
            throw new ArgumentOutOfRangeException(nameof(n), n, $"Must be between 0 and the node's level ({Level}).");
        }

        return n == 0 ? this : new HierarchyId(_levels![..^n]);
    }

    /// <summary>
    /// True when this node is <paramref name="parent"/> or below it (SQL Server's
    /// <c>IsDescendantOf</c>, which counts a node as its own descendant).
    /// </summary>
    public bool IsDescendantOf(HierarchyId parent)
    {
        if (parent.Level > Level)
        {
            return false;
        }

        for (var i = 0; i < parent.Level; i++)
        {
            if (!_levels![i].AsSpan().SequenceEqual(parent._levels![i]))
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString()
    {
        if (_levels is null)
        {
            return "/";
        }

        var sb = new StringBuilder("/");
        foreach (var level in _levels)
        {
            for (var j = 0; j < level.Length; j++)
            {
                if (j > 0)
                {
                    sb.Append('.');
                }

                sb.Append(level[j].ToString(CultureInfo.InvariantCulture));
            }

            sb.Append('/');
        }

        return sb.ToString();
    }

    public bool Equals(HierarchyId other)
    {
        if (Level != other.Level)
        {
            return false;
        }

        for (var i = 0; i < Level; i++)
        {
            if (!_levels![i].AsSpan().SequenceEqual(other._levels![i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is HierarchyId other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        if (_levels is not null)
        {
            foreach (var level in _levels)
            {
                foreach (var component in level)
                {
                    hash.Add(component);
                }

                hash.Add(long.MinValue); // level separator
            }
        }

        return hash.ToHashCode();
    }

    /// <summary>Orders nodes as SQL Server does: depth-first, a parent before its children.</summary>
    public int CompareTo(HierarchyId other) =>
        ToSqlServerBytes().AsSpan().SequenceCompareTo(other.ToSqlServerBytes());

    public static bool operator ==(HierarchyId left, HierarchyId right) => left.Equals(right);
    public static bool operator !=(HierarchyId left, HierarchyId right) => !left.Equals(right);
    public static bool operator <(HierarchyId left, HierarchyId right) => left.CompareTo(right) < 0;
    public static bool operator >(HierarchyId left, HierarchyId right) => left.CompareTo(right) > 0;
    public static bool operator <=(HierarchyId left, HierarchyId right) => left.CompareTo(right) <= 0;
    public static bool operator >=(HierarchyId left, HierarchyId right) => left.CompareTo(right) >= 0;

    // ---------------------------------------------------------------------------------------
    // SQL Server's OrdPath encoding. Each component is a prefix naming its range, the value's
    // offset within that range spread over the 'x' bits (most significant first, the fixed 0/1
    // bits in between keep the encodings ordered), and a final bit: 1 ends the level ('/'), 0
    // means a '.' follows, in which case the component is stored as value + 1 so that "1.x"
    // sorts after "1" and before "2". The bytes are zero-padded. Every pattern below was checked
    // against SQL Server 2025's own encoding (HierarchyIdTests).
    // ---------------------------------------------------------------------------------------

    private sealed record Pattern(long Min, long Max, string Bits);

    private static readonly Pattern[] Patterns =
    {
        new(-281479271682120, -4294971465, "000100" + new string('x', 14) + "0" + new string('x', 21) + "0xxxxxx0xxx0x1xxxT"),
        new(-4294971464, -4169, "000101" + new string('x', 19) + "0xxxxxx0xxx0x1xxxT"),
        new(-4168, -73, "000110xxxxx0xxx0x1xxxT"),
        new(-72, -9, "0010xx0x1xxxT"),
        new(-8, -1, "00111xxxT"),
        new(0, 3, "01xxT"),
        new(4, 7, "100xxT"),
        new(8, 15, "101xxxT"),
        new(16, 79, "110xx0x1xxxT"),
        new(80, 1103, "1110xxx0xxx0x1xxxT"),
        new(1104, 5199, "11110xxxxx0xxx0x1xxxT"),
        new(5200, 4294972495, "111110" + new string('x', 19) + "0xxxxxx0xxx0x1xxxT"),
        new(4294972496, 281479271683151, "111111" + new string('x', 14) + "0" + new string('x', 21) + "0xxxxxx0xxx0x1xxxT"),
    };

    /// <summary>Encodes the node as SQL Server stores it (the root is empty).</summary>
    public byte[] ToSqlServerBytes()
    {
        if (_levels is null)
        {
            return Array.Empty<byte>();
        }

        var bits = new List<bool>();
        foreach (var level in _levels)
        {
            for (var j = 0; j < level.Length; j++)
            {
                var last = j == level.Length - 1;
                var stored = last ? level[j] : level[j] + 1;
                var pattern = Array.Find(Patterns, p => stored >= p.Min && stored <= p.Max)!;
                var offset = (ulong)(stored - pattern.Min);
                var valueBits = pattern.Bits.Count(c => c == 'x');
                var next = valueBits - 1;
                foreach (var c in pattern.Bits)
                {
                    switch (c)
                    {
                        case 'x':
                            bits.Add(((offset >> next) & 1) == 1);
                            next--;
                            break;
                        case 'T':
                            bits.Add(last);
                            break;
                        default:
                            bits.Add(c == '1');
                            break;
                    }
                }
            }
        }

        var bytes = new byte[(bits.Count + 7) / 8];
        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i])
            {
                bytes[i / 8] |= (byte)(0x80 >> (i % 8));
            }
        }

        return bytes;
    }

    /// <summary>Decodes SQL Server's stored encoding of a hierarchyid.</summary>
    /// <exception cref="FormatException">The bytes aren't a hierarchyid encoding.</exception>
    public static HierarchyId FromSqlServerBytes(ReadOnlySpan<byte> bytes)
    {
        var data = bytes.ToArray();
        var totalBits = data.Length * 8;
        bool Bit(int i) => (data[i / 8] & (0x80 >> (i % 8))) != 0;
        bool RestIsZero(int from)
        {
            for (var i = from; i < totalBits; i++)
            {
                if (Bit(i))
                {
                    return false;
                }
            }

            return true;
        }

        var levels = new List<long[]>();
        var current = new List<long>();
        var pos = 0;
        while (!RestIsZero(pos))
        {
            var pattern = Array.Find(Patterns, p => Matches(p.Bits, pos))
                          ?? throw new FormatException($"No hierarchyid label pattern at bit {pos}.");
            ulong offset = 0;
            var last = false;
            foreach (var c in pattern.Bits)
            {
                if (pos >= totalBits)
                {
                    throw new FormatException("The hierarchyid encoding ends inside a label.");
                }

                var bit = Bit(pos++);
                switch (c)
                {
                    case 'x':
                        offset = (offset << 1) | (bit ? 1UL : 0UL);
                        break;
                    case 'T':
                        last = bit;
                        break;
                    default:
                        if (bit != (c == '1'))
                        {
                            throw new FormatException($"Invalid hierarchyid encoding at bit {pos - 1}.");
                        }

                        break;
                }
            }

            var value = pattern.Min + (long)offset;
            current.Add(last ? value : value - 1);
            if (last)
            {
                levels.Add(current.ToArray());
                current.Clear();
            }
        }

        if (current.Count > 0)
        {
            throw new FormatException("The hierarchyid encoding ends inside a level.");
        }

        return new HierarchyId(levels.ToArray());

        bool Matches(string patternBits, int at)
        {
            for (var i = 0; i < patternBits.Length && patternBits[i] is '0' or '1'; i++)
            {
                if (at + i >= totalBits || Bit(at + i) != (patternBits[i] == '1'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
