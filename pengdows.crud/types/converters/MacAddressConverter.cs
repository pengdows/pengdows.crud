// =============================================================================
// FILE: MacAddressConverter.cs
// PURPOSE: Converter for PostgreSQL MACADDR type (hardware MAC address).
//
// AI SUMMARY:
// - Converts between database macaddr values and MacAddress value objects.
// - Supports 48-bit (6-byte) and 64-bit (8-byte) MAC addresses.
// - Provider-specific:
//   * PostgreSQL: MACADDR or MACADDR8 type ("08:00:2b:01:02:03")
//   * Others: Store as VARCHAR or BINARY
// - ConvertToProvider(): Returns string for PostgreSQL, PhysicalAddress otherwise.
// - TryConvertFromProvider(): Handles MacAddress, string, PhysicalAddress, NpgsqlMacAddress.
// - Accepts colon-separated, hyphen-separated, or raw hex formats.
// - Output standardized to colon-separated format.
// - Thread-safe and immutable value objects.
// =============================================================================

using System.Net.NetworkInformation;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

/// <summary>
/// Converts between database MAC address values and <see cref="MacAddress"/> value objects.
/// Supports standard 48-bit (6-byte) and 64-bit (8-byte) MAC addresses.
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific behavior:</strong></para>
/// <list type="bullet">
/// <item><description><strong>PostgreSQL:</strong> Maps to MACADDR or MACADDR8 types. Format: "08:00:2b:01:02:03" or "08-00-2b-01-02-03".</description></item>
/// <item><description><strong>Other databases:</strong> No native MAC address type. Store as VARCHAR or BINARY with application-level parsing.</description></item>
/// </list>
/// <para><strong>Supported conversions from database:</strong></para>
/// <list type="bullet">
/// <item><description>MacAddress → MacAddress (pass-through)</description></item>
/// <item><description>string → MacAddress (parses "08:00:2b:01:02:03", "08-00-2b-01-02-03", or "08002b010203")</description></item>
/// <item><description>PhysicalAddress → MacAddress (wraps .NET PhysicalAddress)</description></item>
/// <item><description>NpgsqlMacAddress → MacAddress (converts Npgsql provider-specific type via reflection)</description></item>
/// </list>
/// <para><strong>Format flexibility:</strong> Accepts colon-separated, hyphen-separated, or raw hex formats.
/// Output format is standardized to colon-separated (e.g., "08:00:2b:01:02:03").</para>
/// <para><strong>Thread safety:</strong> Converter instances are thread-safe. MacAddress value objects are immutable and thread-safe.</para>
/// </remarks>
/// <example>
/// <code>
/// // Entity with MAC address
/// [Table("network_devices")]
/// public class NetworkDevice
/// {
///     [Id]
///     [Column("id", DbType.Int32)]
///     public int Id { get; set; }
///
///     [Column("mac_address", DbType.Object)]
///     public MacAddress MacAddress { get; set; }
/// }
///
/// // Create with MAC address
/// var device = new NetworkDevice
/// {
///     MacAddress = MacAddress.Parse("08:00:2b:01:02:03")
/// };
/// await helper.CreateAsync(device);
///
/// // Also accepts hyphen-separated format
/// var device2 = new NetworkDevice
/// {
///     MacAddress = MacAddress.Parse("08-00-2b-01-02-03")
/// };
/// await helper.CreateAsync(device2);
///
/// // Retrieve and use
/// var retrieved = await helper.RetrieveOneAsync(device.Id);
/// Console.WriteLine($"MAC: {retrieved.MacAddress}");  // "08:00:2b:01:02:03"
/// </code>
/// </example>
internal sealed class MacAddressConverter : AdvancedTypeConverter<MacAddress>
{
    protected override object? ConvertToProvider(MacAddress value, SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.PostgreSql => value.ToString(),
            _ => value.Address
        };
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out MacAddress result)
    {
        if (value is MacAddress mac)
        {
            result = mac;
            return true;
        }

        if (value is string text)
        {
            try
            {
                result = MacAddress.Parse(text);
                return true;
            }
            catch
            {
                result = default!;
                return false;
            }
        }

        if (value is PhysicalAddress physical)
        {
            result = new MacAddress(physical);
            return true;
        }

        var type = value.GetType();
        if (type.FullName?.Contains("NpgsqlMacAddress", StringComparison.OrdinalIgnoreCase) == true)
        {
            var addressProp = type.GetProperty("Address");
            if (addressProp?.GetValue(value) is PhysicalAddress address)
            {
                result = new MacAddress(address);
                return true;
            }
        }

        result = default!;
        return false;
    }
}