// =============================================================================
// FILE: CidrConverter.cs
// PURPOSE: Converter for PostgreSQL CIDR type (network subnet address).
//
// AI SUMMARY:
// - Converts between database cidr values and Cidr value objects.
// - Represents IPv4/IPv6 network subnets in CIDR notation (prefix required).
// - CIDR vs INET: CIDR requires prefix and enforces host bits = zero.
// - Provider-specific:
//   * PostgreSQL/CockroachDB: CIDR type ("192.168.0.0/16")
//   * Others: Store as VARCHAR
// - ConvertToProvider(): Returns string for PostgreSQL, raw value otherwise.
// - TryConvertFromProvider(): Handles Cidr, string, NpgsqlCidr, and an Inet with its prefix.
// - Text is parsed by Cidr.Parse (one parser, DRY-012).
// - Thread-safe and immutable value objects.
// =============================================================================

using System.Net;
using pengdows.crud.@internal;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

/// <summary>
/// Converts between database network subnet values and <see cref="Cidr"/> value objects.
/// Represents IPv4/IPv6 network subnets in CIDR notation (always includes prefix).
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific behavior:</strong></para>
/// <list type="bullet">
/// <item><description><strong>PostgreSQL:</strong> Maps to CIDR type. Enforces proper network address (host bits must be zero). Format: "192.168.1.0/24".</description></item>
/// <item><description><strong>CockroachDB:</strong> Maps to CIDR type (PostgreSQL compatible).</description></item>
/// <item><description><strong>Other databases:</strong> No native CIDR type. Store as VARCHAR with application-level validation.</description></item>
/// </list>
/// <para><strong>Supported conversions from database:</strong></para>
/// <list type="bullet">
/// <item><description>Cidr → Cidr (pass-through)</description></item>
/// <item><description>string → Cidr (parses "192.168.0.0/16" or "2001:db8::/32", requires prefix)</description></item>
/// <item><description>NpgsqlCidr → Cidr (converts Npgsql provider-specific type via reflection)</description></item>
/// </list>
/// <para><strong>Difference from INET:</strong> CIDR always requires a prefix and represents a network range,
/// not a specific host. INET can represent a host address without a prefix. In PostgreSQL, CIDR enforces
/// that host bits are zero (e.g., "192.168.1.5/24" is invalid for CIDR, must be "192.168.1.0/24").</para>
/// <para><strong>Thread safety:</strong> Converter instances are thread-safe. Cidr value objects are immutable and thread-safe.</para>
/// </remarks>
/// <example>
/// <code>
/// // Entity with network subnet
/// [Table("subnets")]
/// public class Subnet
/// {
///     [Id]
///     [Column("id", DbType.Int32)]
///     public int Id { get; set; }
///
///     [Column("network", DbType.Object)]
///     public Cidr Network { get; set; }
/// }
///
/// // Create with IPv4 subnet
/// var subnet = new Subnet
/// {
///     Network = Cidr.Parse("192.168.0.0/16")
/// };
/// await helper.CreateAsync(subnet);
///
/// // Create with IPv6 subnet
/// var subnet2 = new Subnet
/// {
///     Network = Cidr.Parse("2001:db8::/32")
/// };
/// await helper.CreateAsync(subnet2);
///
/// // Retrieve and use
/// var retrieved = await helper.RetrieveOneAsync(subnet.Id);
/// Console.WriteLine($"Network: {retrieved.Network}");  // "192.168.0.0/16"
/// Console.WriteLine($"Prefix length: {retrieved.Network.PrefixLength}");  // 16
/// </code>
/// </example>
internal sealed class CidrConverter : AdvancedTypeConverter<Cidr>
{
    protected override object? ConvertToProvider(Cidr value, SupportedDatabase provider)
    {
        // default(Cidr) has no network: it is SQL NULL.
        if (value.Network is null)
        {
            return null;
        }

        // Npgsql binds NpgsqlDbType.Cidr from NpgsqlCidr and rejects a string. A database with no cidr type gets the text.
        return DatabaseTraits.For(provider).BindsNpgsqlCidr ? CreateNpgsqlCidr(value) : value.ToString();
    }

    // NpgsqlTypes.NpgsqlCidr(IPAddress, byte), found by name and compiled once.
    private static readonly Lazy<Func<IPAddress, byte, object>?> NpgsqlCidrCtor = new(() =>
        Type.GetType("NpgsqlTypes.NpgsqlCidr, Npgsql") is { } type
            ? ProviderMembers.Constructor<IPAddress, byte>(type)
            : null);

    private static object CreateNpgsqlCidr(Cidr value) =>
        NpgsqlCidrCtor.Value?.Invoke(value.Network, value.PrefixLength) ?? value.ToString();

    /// <summary>The CIDR an inet names, when it has a prefix.</summary>
    internal static bool TryFromInet(Inet inet, out Cidr result)
    {
        if (inet.PrefixLength is { } prefix)
        {
            result = new Cidr(inet.Address, prefix);
            return true;
        }

        result = default;
        return false;
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out Cidr result)
    {
        // An address with its prefix (a column the database types as inet, read through Npgsql's typed inet) is the
        // network it names. A bare address has no prefix, so it is not a CIDR here.
        if (value is Inet inet && TryFromInet(inet, out result))
        {
            return true;
        }

        if (value is Cidr cidr)
        {
            result = cidr;
            return true;
        }

        if (value is string text)
        {
            // A malformed CIDR is an error with a reason (the parser's message), not "not a CIDR": let it propagate.
            result = Cidr.Parse(text);
            return true;
        }

        var type = value.GetType();
        // By shape (an Address property, as NpgsqlInet/NpgsqlCidr and similar provider types have), not
        // by type name: the coercion this replaced matched by shape, and Npgsql 8 added NpgsqlCidr (DRY-010).
        if (ProviderAddress.Of(type) is { } shape)
        {
            try
            {
                var addressValue = shape.Address(value);
                var prefix = shape.Prefix?.Invoke(value);
                if (addressValue != null && prefix != null)
                {
                    result = new Cidr(addressValue, prefix.Value);
                    return true;
                }
            }
            catch (Exception ex) when (ex is InvalidCastException or System.Reflection.TargetInvocationException)
            {
                // The provider type's Address/Prefix is not the shape this converter reads: not convertible, so fall through to false.
            }
        }

        result = default!;
        return false;
    }
}