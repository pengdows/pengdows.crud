// =============================================================================
// FILE: Inet.cs
// PURPOSE: Immutable value object for PostgreSQL INET type (IP address).
//
// AI SUMMARY:
// - Represents an IP address with optional CIDR prefix (like "192.168.1.1/24").
// - Readonly struct implementing IEquatable<Inet>.
// - Properties:
//   * Address: IPAddress - the IP address
//   * PrefixLength: byte? - optional CIDR prefix (null = no prefix)
// - Validates prefix length against address family (32 for IPv4, 128 for IPv6).
// - Parse(): Parses "ip" or "ip/prefix" format strings.
// - ToString(): Returns "ip" or "ip/prefix" format.
// - Differs from Cidr: Inet can omit prefix, Cidr requires it.
// - Thread-safe and immutable.
// =============================================================================

using System.Net;

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// Immutable value object representing an IP address with optional CIDR prefix.
/// </summary>
/// <remarks>
/// Maps to PostgreSQL INET type. Supports IPv4 and IPv6 addresses.
/// Unlike <see cref="Cidr"/>, the prefix length is optional.
/// </remarks>
public readonly struct Inet : IEquatable<Inet>
{
    public Inet(IPAddress address, byte? prefixLength = null)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));

        if (prefixLength.HasValue)
        {
            var max = address.AddressFamily switch
            {
                System.Net.Sockets.AddressFamily.InterNetwork => 32,
                System.Net.Sockets.AddressFamily.InterNetworkV6 => 128,
                _ => throw new ArgumentException("Unsupported address family for inet", nameof(address))
            };

            if (prefixLength.Value > max)
            {
                throw new ArgumentOutOfRangeException(nameof(prefixLength), prefixLength.Value,
                    "Prefix length exceeds address family bounds.");
            }
        }

        PrefixLength = prefixLength;
    }

    public IPAddress Address { get; }
    public byte? PrefixLength { get; }

    public override string ToString()
    {
        return PrefixLength.HasValue
            ? string.Concat(Address, "/",
                PrefixLength.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : Address.ToString();
    }

    public bool Equals(Inet other)
    {
        if (Address is null && other.Address is null)
        {
            return PrefixLength == other.PrefixLength;
        }

        if (Address is null || other.Address is null)
        {
            return false;
        }

        return Address.Equals(other.Address) && PrefixLength == other.PrefixLength;
    }

    public override bool Equals(object? obj)
    {
        return obj is Inet other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Address, PrefixLength);
    }

    public static Inet Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("inet value cannot be empty.");
        }

        var parts = text.Split('/', 2);
        var address = IPAddress.Parse(parts[0]);
        byte? prefix = null;
        if (parts.Length == 2 && !string.IsNullOrEmpty(parts[1]))
        {
            prefix = byte.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        }

        return new Inet(address, prefix);
    }
}