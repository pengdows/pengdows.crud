using System;
using System.Net.NetworkInformation;
using System.Text;

namespace pengdows.crud.types.valueobjects;

public readonly struct MacAddress : IEquatable<MacAddress>
{
    public MacAddress(PhysicalAddress address)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));
    }

    public PhysicalAddress Address { get; }

    public override string ToString()
    {
        var bytes = Address.GetAddressBytes();
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(bytes.Length * 3 - 1);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(':');
            }

            sb.Append(bytes[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    public bool Equals(MacAddress other)
    {
        if (Address is null && other.Address is null)
            return true;
        if (Address is null || other.Address is null)
            return false;
        return Address.Equals(other.Address);
    }

    public override bool Equals(object? obj)
    {
        return obj is MacAddress other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Address.GetHashCode();
    }

    public static MacAddress Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("MAC address cannot be empty", nameof(value));
        }

        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        if (normalized.Length % 2 != 0)
        {
            throw new FormatException("Invalid MAC address length.");
        }

        var bytes = new byte[normalized.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var hex = normalized.Substring(i * 2, 2);
            bytes[i] = Convert.ToByte(hex, 16);
        }

        return new MacAddress(new PhysicalAddress(bytes));
    }
}
