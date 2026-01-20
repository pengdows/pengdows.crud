using System;
using System.Net;
using pengdows.crud.enums;
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
        return provider switch
        {
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb => value.ToString(),
            _ => value
        };
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out Cidr result)
    {
        if (value is Cidr cidr)
        {
            result = cidr;
            return true;
        }

        if (value is string text)
        {
            try
            {
                result = Parse(text);
                return true;
            }
            catch
            {
                result = default!;
                return false;
            }
        }

        var type = value.GetType();
        if (type.FullName?.Contains("NpgsqlCidr", StringComparison.OrdinalIgnoreCase) == true)
        {
            var addressProp = type.GetProperty("Address");
            var netmaskProp = type.GetProperty("Netmask");
            var addressValue = addressProp?.GetValue(value) as IPAddress;
            var maskValue = netmaskProp?.GetValue(value);
            if (addressValue != null && maskValue != null)
            {
                try
                {
                    var prefix = Convert.ToByte(maskValue, System.Globalization.CultureInfo.InvariantCulture);
                    result = new Cidr(addressValue, prefix);
                    return true;
                }
                catch
                {
                    // Fall through to return false
                }
            }
        }

        result = default!;
        return false;
    }

    private static Cidr Parse(string text)
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
}
