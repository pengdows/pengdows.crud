using System.Data;
using pengdows.crud.types.coercion;

namespace pengdows.crud.@internal;

/// <summary>
/// The typed reads of a double or float column. A driver can reject the text of a non-finite number in its typed getter
/// (Snowflake throws FormatException on "inf" from GetDouble, GetFloat and GetValue, and returns it from GetString); the
/// text is then read and parsed through <see cref="FloatingPointText"/>, so the infinity reads as the infinity.
/// </summary>
internal static class FloatingPointReads
{
    public static double ReadDouble(IDataRecord record, int ordinal)
    {
        try
        {
            return record.GetDouble(ordinal);
        }
        catch (FormatException)
        {
            return FloatingPointText.ParseDoubleOrThrow(record.GetString(ordinal));
        }
    }

    public static float ReadSingle(IDataRecord record, int ordinal)
    {
        try
        {
            return record.GetFloat(ordinal);
        }
        catch (FormatException)
        {
            return FloatingPointText.ParseSingleOrThrow(record.GetString(ordinal));
        }
    }
}
