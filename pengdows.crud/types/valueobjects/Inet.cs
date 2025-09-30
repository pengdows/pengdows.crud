using System;
using System.Net;

namespace pengdows.crud.types.valueobjects;

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
                throw new ArgumentOutOfRangeException(nameof(prefixLength), prefixLength.Value, "Prefix length exceeds address family bounds.");
            }
        }

        PrefixLength = prefixLength;
    }

    public IPAddress Address { get; }
    public byte? PrefixLength { get; }

    public override string ToString()
    {
        return PrefixLength.HasValue
            ? string.Concat(Address, "/", PrefixLength.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : Address.ToString();
    }

    public bool Equals(Inet other)
    {
        if (Address is null && other.Address is null)
            return PrefixLength == other.PrefixLength;
        if (Address is null || other.Address is null)
            return false;
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
}
