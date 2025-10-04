using System;
using System.Net.NetworkInformation;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

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
