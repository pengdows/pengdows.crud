using System;
using System.Net;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

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

    protected override bool TryConvertFromProvider(object value, SupportedDatabase provider, out Inet result)
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
