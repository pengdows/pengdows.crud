// =============================================================================
// FILE: IPAddressConverter.cs
// PURPOSE: Converter for System.Net.IPAddress, the address type the fidelity matrix binds for "IP address".
//
// AI SUMMARY:
// - ConvertToProvider(): the Npgsql-family databases take the address value (Npgsql maps it to inet); every other
//   database takes the canonical text, since none of them has an address type with that CLR type.
// - TryConvertFromProvider(): an IPAddress, its text, or its 4- or 16-byte form.
// - Thread-safe: stateless.
// =============================================================================

using System.Net;
using System.Net.Sockets;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.types.converters;

/// <summary>Converts <see cref="IPAddress"/> to and from the database's address text or bytes.</summary>
internal sealed class IPAddressConverter : AdvancedTypeConverter<IPAddress>
{
    protected override object? ConvertToProvider(IPAddress value, SupportedDatabase provider)
    {
        return DatabaseTraits.For(provider).BindsNpgsqlValueTypes ? value : value.ToString();
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out IPAddress result)
    {
        switch (value)
        {
            case IPAddress address:
                result = address;
                return true;
            case string text when IPAddress.TryParse(text.Trim(), out var parsed):
                result = parsed;
                return true;
            case byte[] bytes when bytes.Length is 4 or 16:
                result = new IPAddress(bytes);
                return true;
            default:
                result = default!;
                return false;
        }
    }
}
