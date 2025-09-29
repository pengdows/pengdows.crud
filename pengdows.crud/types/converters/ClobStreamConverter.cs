using System;
using System.IO;
using System.Text;
using pengdows.crud.enums;

namespace pengdows.crud.types.converters;

internal sealed class ClobStreamConverter : AdvancedTypeConverter<TextReader>
{
    protected override object? ConvertToProvider(TextReader value, SupportedDatabase provider)
    {
        return value;
    }

    protected override bool TryConvertFromProvider(object value, SupportedDatabase provider, out TextReader result)
    {
        switch (value)
        {
            case TextReader reader:
                result = reader;
                return true;
            case string text:
                result = new StringReader(text);
                return true;
            case Stream stream:
                try
                {
                    result = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                    return true;
                }
                catch
                {
                    result = default!;
                    return false;
                }
            case ReadOnlyMemory<char> memory:
                result = new StringReader(memory.ToString());
                return true;
            default:
                result = default!;
                return false;
        }
    }
}
