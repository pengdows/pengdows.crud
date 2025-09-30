using System;
using System.IO;
using pengdows.crud.enums;

namespace pengdows.crud.types.converters;

internal sealed class BlobStreamConverter : AdvancedTypeConverter<Stream>
{
    protected override object? ConvertToProvider(Stream value, SupportedDatabase provider)
    {
        if (value.CanSeek)
        {
            value.Seek(0, SeekOrigin.Begin);
        }

        return value;
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out Stream result)
    {
        switch (value)
        {
            case Stream stream:
                try
                {
                    if (stream.CanSeek)
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                    }
                    result = stream;
                    return true;
                }
                catch
                {
                    result = default!;
                    return false;
                }
            case byte[] bytes:
                result = new MemoryStream(bytes, writable: false);
                return true;
            case ReadOnlyMemory<byte> memory:
                result = new MemoryStream(memory.ToArray(), writable: false);
                return true;
            case ArraySegment<byte> segment:
                if (segment.Array != null)
                {
                    result = new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false);
                    return true;
                }
                result = default!;
                return false;
            default:
                result = default!;
                return false;
        }
    }
}
