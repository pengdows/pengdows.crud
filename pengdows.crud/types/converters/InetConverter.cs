using System;
using System.Net;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

/// <summary>
/// Converts between database network address values and <see cref="Inet"/> value objects.
/// Supports IPv4 and IPv6 addresses with optional CIDR prefix notation.
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific behavior:</strong></para>
/// <list type="bullet">
/// <item><description><strong>PostgreSQL:</strong> Maps to INET type. Supports IPv4/IPv6 with optional subnet prefix. Format: "192.168.1.1/24" or "2001:db8::1/64".</description></item>
/// <item><description><strong>CockroachDB:</strong> Maps to INET type (PostgreSQL compatible).</description></item>
/// <item><description><strong>Other databases:</strong> No native inet type. Store as VARCHAR with application-level parsing.</description></item>
/// </list>
/// <para><strong>Supported conversions from database:</strong></para>
/// <list type="bullet">
/// <item><description>Inet → Inet (pass-through)</description></item>
/// <item><description>string → Inet (parses "192.168.1.1" or "192.168.1.1/24" or "2001:db8::1/64")</description></item>
/// <item><description>IPAddress → Inet (wraps .NET IPAddress)</description></item>
/// <item><description>NpgsqlInet → Inet (converts Npgsql provider-specific type via reflection)</description></item>
/// </list>
/// <para><strong>Format:</strong> Supports standard INET notation with optional CIDR prefix.
/// Examples: "192.168.1.5", "10.0.0.1/8", "2001:db8::8a2e:370:7334/128".</para>
/// <para><strong>Thread safety:</strong> Converter instances are thread-safe. Inet value objects are immutable and thread-safe.</para>
/// </remarks>
/// <example>
/// <code>
/// // Entity with network address
/// [Table("servers")]
/// public class Server
/// {
///     [Id]
///     [Column("id", DbType.Int32)]
///     public int Id { get; set; }
///
///     [Column("ip_address", DbType.Object)]
///     public Inet IpAddress { get; set; }
/// }
///
/// // Create with IPv4 address
/// var server = new Server
/// {
///     IpAddress = Inet.Parse("192.168.1.100/24")
/// };
/// await helper.CreateAsync(server);
///
/// // Create with IPv6 address
/// var server2 = new Server
/// {
///     IpAddress = Inet.Parse("2001:db8::1/64")
/// };
/// await helper.CreateAsync(server2);
///
/// // Retrieve and use
/// var retrieved = await helper.RetrieveOneAsync(server.Id);
/// Console.WriteLine($"IP: {retrieved.IpAddress}");  // "192.168.1.100/24"
/// Console.WriteLine($"Prefix: {retrieved.IpAddress.Prefix}");  // 24
/// </code>
/// </example>
internal sealed class InetConverter : AdvancedTypeConverter<Inet>
{
    protected override object? ConvertToProvider(Inet value, SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb => value.ToString(),
            _ => value
        };
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out Inet result)
    {
        if (value is Inet inet)
        {
            result = inet;
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

        if (value is IPAddress address)
        {
            result = new Inet(address);
            return true;
        }

        var type = value.GetType();
        if (type.FullName?.Contains("NpgsqlInet", StringComparison.OrdinalIgnoreCase) == true)
        {
            var addressProp = type.GetProperty("Address");
            var netmaskProp = type.GetProperty("Netmask");
            var addressValue = addressProp?.GetValue(value) as IPAddress;
            var maskValue = netmaskProp?.GetValue(value);
            byte? prefix = null;
            if (maskValue != null)
            {
                prefix = Convert.ToByte(maskValue, System.Globalization.CultureInfo.InvariantCulture);
            }

            if (addressValue != null)
            {
                result = new Inet(addressValue, prefix);
                return true;
            }
        }

        result = default!;
        return false;
    }

    private static Inet Parse(string text)
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