namespace pengdows.crud.@internal;

/// <summary>
/// The numeric CLR types every read path converts between (DRY-005: four copies of this switch).
/// By type code, so an enum counts as its underlying integer.
/// </summary>
internal static class NumericTypes
{
    public static bool IsNumeric(Type type) => Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte
        or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64
        or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    public static bool IsIntegral(Type type) => Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte
        or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64
        or TypeCode.UInt64;
}
