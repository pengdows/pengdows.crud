using System;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

internal sealed class RowVersionConverter : AdvancedTypeConverter<RowVersion>
{
    protected override object? ConvertToProvider(RowVersion value, SupportedDatabase provider)
    {
        return value.ToArray();
    }

    protected override bool TryConvertFromProvider(object value, SupportedDatabase provider, out RowVersion result)
    {
        switch (value)
        {
            case RowVersion rv:
                result = rv;
                return true;
            case byte[] bytes:
                try
                {
                    result = RowVersion.FromBytes(bytes);
                    return true;
                }
                catch
                {
                    result = default!;
                    return false;
                }
            case ReadOnlyMemory<byte> memory:
                try
                {
                    result = RowVersion.FromBytes(memory.ToArray());
                    return true;
                }
                catch
                {
                    result = default!;
                    return false;
                }
            case ArraySegment<byte> segment:
                try
                {
                    result = RowVersion.FromBytes(segment.ToArray());
                    return true;
                }
                catch
                {
                    result = default!;
                    return false;
                }
            default:
                result = default!;
                return false;
        }
    }
}
