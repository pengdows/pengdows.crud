// =============================================================================
// FILE: Cidr.cs
// PURPOSE: Immutable value object for PostgreSQL CIDR type (network subnet).
//
// AI SUMMARY:
// - Represents a network subnet in CIDR notation (e.g., "192.168.0.0/16").
// - Readonly struct implementing IEquatable<Cidr>.
// - Properties:
//   * Network: IPAddress - the network address (canonicalized, host bits zeroed)
//   * PrefixLength: byte - required CIDR prefix length
// - Validates prefix length against address family (32 for IPv4, 128 for IPv6).
// - Canonicalize(): Zeros host bits beyond prefix for proper network address.
// - Parse(): Parses "network/prefix" format strings (prefix required).
// - ToString(): Returns "network/prefix" format.
// - Differs from Inet: Cidr requires prefix and enforces host bits = 0.
// - Thread-safe and immutable.
// =============================================================================

using System.Net;

namespace pengdows.crud.types.valueobjects;

/// <summary>
/// Immutable value object representing a network subnet in CIDR notation.
/// </summary>
/// <remarks>
/// Maps to PostgreSQL CIDR type. Unlike <see cref="Inet"/>, the prefix length
/// is required and host bits are automatically zeroed (canonicalized).
/// </remarks>
public readonly struct Cidr : IEquatable<Cidr>
{
    public Cidr(IPAddress network, byte prefixLength)
    {
        if (network is null)
        {
            throw new ArgumentNullException(nameof(network));
        }

        var totalBits = network.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => 32,
            System.Net.Sockets.AddressFamily.InterNetworkV6 => 128,
            _ => throw new ArgumentException("Unsupported address family for CIDR", nameof(network))
        };

        if (prefixLength > totalBits)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength), prefixLength,
                "Prefix length exceeds address family bounds.");
        }

        PrefixLength = prefixLength;
        Network = Canonicalize(network, prefixLength);
    }

    public IPAddress Network { get; }
    public byte PrefixLength { get; }

    public override string ToString()
    {
        return string.Concat(Network, "/", PrefixLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public bool Equals(Cidr other)
    {
        if (Network is null && other.Network is null)
        {
            return PrefixLength == other.PrefixLength;
        }

        if (Network is null || other.Network is null)
        {
            return false;
        }

        return Network.Equals(other.Network) && PrefixLength == other.PrefixLength;
    }

    public override bool Equals(object? obj)
    {
        return obj is Cidr other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Network, PrefixLength);
    }

    public static Cidr Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("cidr value cannot be empty.");
        }

        var parts = text.Split('/', 2);
        if (parts.Length != 2)
        {
            throw new FormatException("cidr requires network/prefix format.");
        }

        var address = IPAddress.Parse(parts[0]);
        var prefix = byte.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        return new Cidr(address, prefix);
    }

    private static IPAddress Canonicalize(IPAddress address, byte prefixLength)
    {
        var bytes = address.GetAddressBytes();
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        if (fullBytes < bytes.Length)
        {
            for (var i = fullBytes + (remainingBits > 0 ? 1 : 0); i < bytes.Length; i++)
            {
                bytes[i] = 0;
            }
        }

        if (remainingBits > 0)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            bytes[fullBytes] = (byte)(bytes[fullBytes] & mask);
        }

        return new IPAddress(bytes);
    }
}