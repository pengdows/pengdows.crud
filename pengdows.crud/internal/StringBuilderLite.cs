#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;

namespace pengdows.crud.@internal;

internal ref struct StringBuilderLite
{
    private char[]? _heap;
    private Span<char> _buf;
    private int _pos;

    public StringBuilderLite(Span<char> initial)
    {
        _heap = null;
        _buf = initial;
        _pos = 0;
    }

    public int Length => _pos;

    public void Clear() => _pos = 0;

    public void Append(char c)
    {
        int p = _pos;
        if ((uint)p < (uint)_buf.Length)
        {
            _buf[p] = c;
            _pos = p + 1;
            return;
        }

        Grow(p + 1);
        _buf[_pos++] = c;
    }

    public void Append(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return;

        Append(s.AsSpan());
    }

    public void Append(scoped ReadOnlySpan<char> s)
    {
        int newLen = _pos + s.Length;
        if (newLen > _buf.Length)
            Grow(newLen);

        s.CopyTo(_buf.Slice(_pos));
        _pos = newLen;
    }

    public void AppendLine() => Append('\n');

    public void AppendLine(string? s)
    {
        Append(s);
        Append('\n');
    }

    public Span<char> AppendSpan(int length)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        int newLen = _pos + length;
        if (newLen > _buf.Length)
            Grow(newLen);

        var span = _buf.Slice(_pos, length);
        _pos = newLen;
        return span;
    }

    public void Append(int value)
    {
        Span<char> tmp = stackalloc char[11];
        if (!value.TryFormat(tmp, out int written, provider: CultureInfo.InvariantCulture))
        {
            Append(value.ToString(CultureInfo.InvariantCulture));
            return;
        }

        Append(tmp.Slice(0, written));
    }

    public void Append(long value)
    {
        Span<char> tmp = stackalloc char[20];
        if (!value.TryFormat(tmp, out int written, provider: CultureInfo.InvariantCulture))
        {
            Append(value.ToString(CultureInfo.InvariantCulture));
            return;
        }

        Append(tmp.Slice(0, written));
    }

    public override string ToString()
        => _buf.Slice(0, _pos).ToString();

    private void Grow(int minCapacity)
    {
        Debug.Assert(minCapacity > _buf.Length);

        int newCap = _buf.Length == 0 ? 256 : _buf.Length * 2;
        if (newCap < minCapacity)
            newCap = minCapacity;

        var arr = new char[newCap];
        _buf.Slice(0, _pos).CopyTo(arr);

        _heap = arr;
        _buf = arr;
    }
}
