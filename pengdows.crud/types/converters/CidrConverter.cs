using System;
using System.Net;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

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
