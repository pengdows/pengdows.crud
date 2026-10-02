using System.Globalization;

namespace pengdows.crud.fakeDb;

/// <summary>
/// A provider-specific decimal returned by <see cref="fakeDbDataReader.GetValue"/> for
/// <see cref="fakeDbDataReader.ProviderDecimalColumns"/>, as Sap.Data.Hana.Net's HanaDecimal is:
/// <see cref="IConvertible"/>, with <see cref="ToDecimal()"/>, but not a <see cref="decimal"/>.
/// </summary>
public sealed class fakeDbProviderDecimal : IConvertible
{
    private readonly decimal _value;

    public fakeDbProviderDecimal(decimal value) => _value = value;

    public decimal ToDecimal() => _value;

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);

    public TypeCode GetTypeCode() => TypeCode.Object;
    public bool ToBoolean(IFormatProvider? provider) => _value != 0m;
    public byte ToByte(IFormatProvider? provider) => (byte)_value;
    public char ToChar(IFormatProvider? provider) => throw new InvalidCastException();
    public DateTime ToDateTime(IFormatProvider? provider) => throw new InvalidCastException();
    public decimal ToDecimal(IFormatProvider? provider) => _value;
    public double ToDouble(IFormatProvider? provider) => (double)_value;
    public short ToInt16(IFormatProvider? provider) => (short)_value;
    public int ToInt32(IFormatProvider? provider) => (int)_value;
    public long ToInt64(IFormatProvider? provider) => (long)_value;
    public sbyte ToSByte(IFormatProvider? provider) => (sbyte)_value;
    public float ToSingle(IFormatProvider? provider) => (float)_value;
    public string ToString(IFormatProvider? provider) => _value.ToString(provider);
    public object ToType(Type conversionType, IFormatProvider? provider) =>
        Convert.ChangeType(_value, conversionType, provider);
    public ushort ToUInt16(IFormatProvider? provider) => (ushort)_value;
    public uint ToUInt32(IFormatProvider? provider) => (uint)_value;
    public ulong ToUInt64(IFormatProvider? provider) => (ulong)_value;
}
