namespace pengdows.crud;

public static class DecimalHelpers
{
    public static (int Precision, int Scale) Infer(decimal value)
    {
        var bits = decimal.GetBits(value);
        var scale = (bits[3] >> 16) & 0x7F;
        var abs = Math.Abs(value);
        var precision = scale;
        for (var temp = decimal.Truncate(abs); temp >= 1; temp /= 10)
        {
            precision++;
        }
        return (precision, scale);
    }
}
