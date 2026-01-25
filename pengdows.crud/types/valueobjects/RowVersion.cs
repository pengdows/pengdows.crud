namespace pengdows.crud.types.valueobjects;

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