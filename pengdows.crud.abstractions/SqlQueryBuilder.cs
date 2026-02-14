using System.Buffers;
using System.Globalization;

namespace pengdows.crud;

/// <summary>
/// High-performance, pooled SQL query builder optimized for repeated appends.
/// </summary>
public sealed class SqlQueryBuilder : IDisposable
{
    private const int DefaultCapacity = 256;
    private char[]? _buffer;
    private int _length;
    private int _version;

    public SqlQueryBuilder()
        : this(DefaultCapacity)
    {
    }

    public SqlQueryBuilder(int capacity)
    {
        var size = capacity <= 0 ? DefaultCapacity : capacity;
        _buffer = ArrayPool<char>.Shared.Rent(size);
    }

    public SqlQueryBuilder(string? initial)
        : this(initial?.Length ?? DefaultCapacity)
    {
        Append(initial);
    }

    /// <summary>
    /// Current length of the query text.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Mutation version for cache invalidation.
    /// </summary>
    public int Version => _version;

    public SqlQueryBuilder Append(char value)
    {
        EnsureCapacity(_length + 1);
        _buffer![_length++] = value;
        _version++;
        return this;
    }

    public SqlQueryBuilder Append(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return this;
        }

        Append(value.AsSpan());
        return this;
    }

    public SqlQueryBuilder Append(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            return this;
        }

        EnsureCapacity(_length + value.Length);
        value.CopyTo(_buffer.AsSpan(_length));
        _length += value.Length;
        _version++;
        return this;
    }

    public SqlQueryBuilder Append(SqlQueryBuilder other)
    {
        if (other == null || other._length == 0)
        {
            return this;
        }

        return Append(other.AsSpan());
    }

    public SqlQueryBuilder CopyFrom(SqlQueryBuilder other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        EnsureCapacity(other._length);

        if (other._length > 0)
        {
            other.AsSpan().CopyTo(_buffer.AsSpan());
        }

        _length = other._length;
        _version = other._version;
        return this;
    }

    public SqlQueryBuilder Append(int value)
    {
        Span<char> scratch = stackalloc char[11];
        if (!value.TryFormat(scratch, out var written, provider: CultureInfo.CurrentCulture))
        {
            return Append(value.ToString(CultureInfo.CurrentCulture));
        }

        return Append(scratch[..written]);
    }

    public SqlQueryBuilder Append(long value)
    {
        Span<char> scratch = stackalloc char[20];
        if (!value.TryFormat(scratch, out var written, provider: CultureInfo.CurrentCulture))
        {
            return Append(value.ToString(CultureInfo.CurrentCulture));
        }

        return Append(scratch[..written]);
    }

    public SqlQueryBuilder Append(double value)
    {
        return Append(value.ToString(CultureInfo.CurrentCulture));
    }

    public SqlQueryBuilder Append(decimal value)
    {
        return Append(value.ToString(CultureInfo.CurrentCulture));
    }

    public SqlQueryBuilder Append(object? value)
    {
        return Append(value?.ToString());
    }

    public SqlQueryBuilder AppendLine()
    {
        return Append('\n');
    }

    public SqlQueryBuilder AppendLine(string? value)
    {
        Append(value);
        return Append('\n');
    }

    public SqlQueryBuilder AppendFormat(string format, params object?[] args)
    {
        if (format == null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        return Append(string.Format(CultureInfo.CurrentCulture, format, args));
    }

    public SqlQueryBuilder AppendFormat(IFormatProvider? provider, string format, params object?[] args)
    {
        if (format == null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        return Append(string.Format(provider ?? CultureInfo.CurrentCulture, format, args));
    }

    public SqlQueryBuilder Replace(string oldValue, string? newValue)
    {
        if (oldValue == null)
        {
            throw new ArgumentNullException(nameof(oldValue));
        }

        if (oldValue.Length == 0)
        {
            throw new ArgumentException("Old value cannot be empty.", nameof(oldValue));
        }

        if (_length == 0)
        {
            return this;
        }

        newValue ??= string.Empty;
        var source = AsSpan();
        var oldSpan = oldValue.AsSpan();

        var firstIndex = source.IndexOf(oldSpan);
        if (firstIndex < 0)
        {
            return this;
        }

        var count = 0;
        var searchIndex = 0;
        while (firstIndex >= 0)
        {
            count++;
            searchIndex = firstIndex + oldSpan.Length;
            if (searchIndex >= source.Length)
            {
                break;
            }

            var next = source.Slice(searchIndex).IndexOf(oldSpan);
            if (next < 0)
            {
                break;
            }

            firstIndex = searchIndex + next;
        }

        var newLength = _length + count * (newValue.Length - oldValue.Length);
        var newBuffer = ArrayPool<char>.Shared.Rent(Math.Max(DefaultCapacity, newLength));

        var read = 0;
        var write = 0;
        var valueSpan = newValue.AsSpan();
        while (read < source.Length)
        {
            var idx = source.Slice(read).IndexOf(oldSpan);
            if (idx < 0)
            {
                source.Slice(read).CopyTo(newBuffer.AsSpan(write));
                write += source.Length - read;
                break;
            }

            var absolute = read + idx;
            source.Slice(read, absolute - read).CopyTo(newBuffer.AsSpan(write));
            write += absolute - read;

            if (valueSpan.Length > 0)
            {
                valueSpan.CopyTo(newBuffer.AsSpan(write));
                write += valueSpan.Length;
            }

            read = absolute + oldSpan.Length;
        }

        var oldBuffer = _buffer;
        _buffer = newBuffer;
        _length = write;
        _version++;

        if (oldBuffer != null)
        {
            ArrayPool<char>.Shared.Return(oldBuffer, clearArray: false);
        }

        return this;
    }

    public SqlQueryBuilder Clear()
    {
        if (_length == 0)
        {
            return this;
        }

        _length = 0;
        _version++;
        return this;
    }

    public override string ToString()
    {
        if (_length == 0)
        {
            return string.Empty;
        }

        return new string(_buffer!, 0, _length);
    }

    public void Dispose()
    {
        if (_buffer == null)
        {
            return;
        }

        ArrayPool<char>.Shared.Return(_buffer, clearArray: false);
        _buffer = null;
        _length = 0;
    }

    internal ReadOnlySpan<char> AsSpan()
    {
        return _length == 0 ? ReadOnlySpan<char>.Empty : _buffer.AsSpan(0, _length);
    }

    private void EnsureCapacity(int required)
    {
        if (_buffer == null)
        {
            _buffer = ArrayPool<char>.Shared.Rent(Math.Max(DefaultCapacity, required));
            return;
        }

        if (required <= _buffer.Length)
        {
            return;
        }

        Grow(required);
    }

    private void Grow(int required)
    {
        var current = _buffer!.Length;
        var target = current == 0 ? DefaultCapacity : current;
        while (target < required)
        {
            target = target * 2;
        }

        var next = ArrayPool<char>.Shared.Rent(target);
        _buffer.AsSpan(0, _length).CopyTo(next);
        ArrayPool<char>.Shared.Return(_buffer, clearArray: false);
        _buffer = next;
    }
}
