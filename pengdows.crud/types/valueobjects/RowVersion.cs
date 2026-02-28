// =============================================================================
// FILE: RowVersion.cs
// PURPOSE: Immutable value object for SQL Server ROWVERSION/TIMESTAMP.
//
// AI SUMMARY:
// - Represents an 8-byte optimistic concurrency token.
// - Readonly struct implementing IEquatable<RowVersion>.
// - Properties:
//   * Value: ReadOnlyMemory<byte> - the 8-byte version value
// - ToArray(): Returns a copy of the internal byte array.
// - FromBytes(): Factory method creating from byte array.
// - Enforces exactly 8 bytes length (SQL Server rowversion size).
// - ToString(): Returns hex string without dashes.
// - Used with [Version] attribute for optimistic concurrency.
// - Thread-safe and immutable.
// =============================================================================

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// Immutable value object representing a SQL Server ROWVERSION/TIMESTAMP.
/// </summary>
/// <remarks>
/// Used for optimistic concurrency control. SQL Server auto-generates these values.
/// With the [Version] attribute, pengdows.crud includes this in UPDATE WHERE clauses.
/// </remarks>
public readonly struct RowVersion : IEquatable<RowVersion>
{
    private const int RequiredLength = 8;
    private readonly byte[] _value;

    public RowVersion(ReadOnlySpan<byte> value)
    {
        if (value.Length != RequiredLength)
        {
            throw new ArgumentException($"RowVersion must be {RequiredLength} bytes.", nameof(value));
        }

        _value = value.ToArray();
    }

    public ReadOnlyMemory<byte> Value => new(_value);

    public byte[] ToArray()
    {
        return (byte[])_value.Clone();
    }

    public bool Equals(RowVersion other)
    {
        if (_value is null && other._value is null)
        {
            return true;
        }

        if (_value is null || other._value is null)
        {
            return false;
        }

        return Value.Span.SequenceEqual(other.Value.Span);
    }

    public override bool Equals(object? obj)
    {
        return obj is RowVersion other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(BitConverter.ToInt64(_value, 0));
    }

    public override string ToString()
    {
        return BitConverter.ToString(_value).Replace("-", string.Empty, StringComparison.Ordinal);
    }

    public static RowVersion FromBytes(byte[] value)
    {
        return new RowVersion(value);
    }
}