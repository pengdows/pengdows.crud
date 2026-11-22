// =============================================================================
// FILE: DuckDbInetReader.cs
// PURPOSE: Reads a DuckDB INET (the inet extension) as an address, a network or an Inet.
//
// AI SUMMARY:
// - DuckDB.NET returns an INET column as a struct dictionary { ip_type, address, mask } (data type name "Struct"), whether or not the
//   extension is loaded on the connection (live, DuckDB 2.0.1). Its text form (CAST(column AS VARCHAR)) only reads as an address
//   while the extension is loaded: on a connection without it, the text is the struct's own. So the struct is decoded here.
// - ip_type 1 is IPv4: address is the unsigned 32-bit value. ip_type 2 is IPv6: address is a HUGEINT, the unsigned 128-bit value
//   less 2^127. mask is the prefix length (32 or 128 for a single host).
// - ReadAddress, ReadCidr and ReadInet return the value for the property's type; a column that is not inet-shaped is returned
//   as GetValue gives it, unchanged. No DuckDB.NET type is referenced.
// =============================================================================

using System.Data;
using System.Net;
using System.Numerics;
using System.Reflection;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.@internal;

internal static class DuckDbInetReader
{
    private static readonly BigInteger Ipv6Offset = BigInteger.Pow(2, 127);

    internal static readonly MethodInfo ReadAddressMethod =
        typeof(DuckDbInetReader).GetMethod(nameof(ReadAddress))!;

    internal static readonly MethodInfo ReadCidrMethod =
        typeof(DuckDbInetReader).GetMethod(nameof(ReadCidr))!;

    internal static readonly MethodInfo ReadInetMethod =
        typeof(DuckDbInetReader).GetMethod(nameof(ReadInet))!;

    /// <summary>The column as an <see cref="IPAddress"/>; the prefix, if any, is dropped.</summary>
    public static object ReadAddress(IDataRecord record, int ordinal)
    {
        var value = record.GetValue(ordinal);
        return TryDecode(value, out var address, out _) ? address : value;
    }

    /// <summary>The column as a <see cref="Cidr"/>: the address and its prefix length.</summary>
    public static object ReadCidr(IDataRecord record, int ordinal)
    {
        var value = record.GetValue(ordinal);
        return TryDecode(value, out var address, out var prefix) ? new Cidr(address, prefix) : value;
    }

    /// <summary>The column as an <see cref="Inet"/>: the address, with a prefix length unless it names a single host.</summary>
    public static object ReadInet(IDataRecord record, int ordinal)
    {
        var value = record.GetValue(ordinal);
        if (!TryDecode(value, out var address, out var prefix))
        {
            return value;
        }

        var host = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        return new Inet(address, prefix == host ? null : prefix);
    }

    /// <summary>Whether <paramref name="value"/> is an inet struct, and its address and prefix length.</summary>
    internal static bool TryDecode(object? value, out IPAddress address, out byte prefix)
    {
        address = IPAddress.None;
        prefix = 0;
        if (value is not IDictionary<string, object?> fields ||
            !fields.TryGetValue("ip_type", out var kind) || kind is null ||
            !fields.TryGetValue("address", out var raw) || raw is null ||
            !fields.TryGetValue("mask", out var mask) || mask is null)
        {
            return false;
        }

        prefix = checked((byte)Convert.ToInt32(mask, System.Globalization.CultureInfo.InvariantCulture));
        switch (Convert.ToInt32(kind, System.Globalization.CultureInfo.InvariantCulture))
        {
            case 1:
                var v4 = checked((uint)ToBigInteger(raw));
                address = new IPAddress(new[]
                {
                    (byte)(v4 >> 24), (byte)(v4 >> 16), (byte)(v4 >> 8), (byte)v4
                });
                return true;
            case 2:
                var bytes = (ToBigInteger(raw) + Ipv6Offset).ToByteArray(isUnsigned: true, isBigEndian: true);
                if (bytes.Length > 16)
                {
                    return false;
                }

                var padded = new byte[16];
                bytes.CopyTo(padded, 16 - bytes.Length);
                address = new IPAddress(padded);
                return true;
            default:
                return false;
        }
    }

    private static BigInteger ToBigInteger(object value) => value switch
    {
        BigInteger big => big,
        Int128 wide => (BigInteger)wide,
        UInt128 wideUnsigned => (BigInteger)wideUnsigned,
        long l => l,
        ulong u => u,
        _ => BigInteger.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!,
            System.Globalization.CultureInfo.InvariantCulture),
    };
}
