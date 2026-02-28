// =============================================================================
// FILE: MacAddress.cs
// PURPOSE: Immutable value object for PostgreSQL MACADDR type.
//
// AI SUMMARY:
// - Represents a MAC hardware address (e.g., "08:00:2b:01:02:03").
// - Readonly struct implementing IEquatable<MacAddress>.
// - Wraps .NET PhysicalAddress type.
// - Properties:
//   * Address: PhysicalAddress - the underlying .NET type
// - Parse(): Accepts colon-separated, hyphen-separated, or raw hex formats.
// - ToString(): Returns colon-separated uppercase format (e.g., "08:00:2B:01:02:03").
// - Uses SbLite for efficient string building.
// - Supports 48-bit (6-byte) and 64-bit (8-byte) addresses.
// - Thread-safe and immutable.
// =============================================================================

using System.Net.NetworkInformation;
using pengdows.crud.@internal;

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// Immutable value object representing a MAC hardware address.
/// </summary>
/// <remarks>
/// Maps to PostgreSQL MACADDR or MACADDR8 types.
/// Accepts multiple input formats and outputs colon-separated format.
/// </remarks>
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

        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
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
        {
            return true;
        }

        if (Address is null || other.Address is null)
        {
            return false;
        }

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