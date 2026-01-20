using System;
using System.Globalization;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

/// <summary>
/// Converts between database interval year-to-month values and <see cref="IntervalYearMonth"/> value objects.
/// Represents Oracle INTERVAL YEAR TO MONTH type with years and months components.
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific behavior:</strong></para>
/// <list type="bullet">
/// <item><description><strong>Oracle:</strong> Maps to INTERVAL YEAR TO MONTH type. Format: P3Y6M (ISO 8601).</description></item>
/// <item><description><strong>PostgreSQL/CockroachDB:</strong> Can be stored as INTERVAL and formatted as ISO 8601.</description></item>
/// <item><description><strong>Other databases:</strong> No native interval year-month type. Application-level storage required.</description></item>
/// </list>
/// <para><strong>Supported conversions from database:</strong></para>
/// <list type="bullet">
/// <item><description>IntervalYearMonth → IntervalYearMonth (pass-through)</description></item>
/// <item><description>string → IntervalYearMonth (parses ISO 8601 duration format like P3Y6M)</description></item>
/// </list>
/// <para><strong>Format:</strong> Uses ISO 8601 duration format for Oracle and PostgreSQL providers.
/// Example: P3Y6M represents 3 years and 6 months.</para>
/// <para><strong>Components:</strong> IntervalYearMonth has Years (integer) and Months (integer, 0-11).
/// This matches Oracle's INTERVAL YEAR TO MONTH semantics.</para>
/// <para><strong>Use case:</strong> Useful for date arithmetic where month/year boundaries matter
/// (e.g., "3 months from today" accounts for varying month lengths, unlike day-based intervals).</para>
/// <para><strong>Thread safety:</strong> Converter instances are thread-safe. IntervalYearMonth value objects are immutable and thread-safe.</para>
/// </remarks>
/// <example>
/// <code>
/// // Entity with year-month interval
/// [Table("subscriptions")]
/// public class Subscription
/// {
///     [Id]
///     [Column("id", DbType.Int32)]
///     public int Id { get; set; }
///
///     [Column("billing_period", DbType.Object)]
///     public IntervalYearMonth BillingPeriod { get; set; }
/// }
///
/// // Create with interval (1 year)
/// var subscription = new Subscription
/// {
///     BillingPeriod = new IntervalYearMonth(years: 1, months: 0)
/// };
/// await helper.CreateAsync(subscription);
///
/// // Create with interval (6 months)
/// var subscription2 = new Subscription
/// {
///     BillingPeriod = new IntervalYearMonth(years: 0, months: 6)
/// };
/// await helper.CreateAsync(subscription2);
///
/// // Retrieve and use
/// var retrieved = await helper.RetrieveOneAsync(subscription.Id);
/// Console.WriteLine($"Years: {retrieved.BillingPeriod.Years}");    // 1
/// Console.WriteLine($"Months: {retrieved.BillingPeriod.Months}");  // 0
/// Console.WriteLine($"Total months: {retrieved.BillingPeriod.TotalMonths}");  // 12
/// </code>
/// </example>
internal sealed class IntervalYearMonthConverter : AdvancedTypeConverter<IntervalYearMonth>
{
    protected override object? ConvertToProvider(IntervalYearMonth value, SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Oracle => FormatIso(value),
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb => FormatIso(value),
            _ => value
        };
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out IntervalYearMonth result)
    {
        if (value is IntervalYearMonth interval)
        {
            result = interval;
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

        result = default!;
        return false;
    }

    private static string FormatIso(IntervalYearMonth value)
    {
        return string.Concat(
            "P",
            value.Years.ToString(CultureInfo.InvariantCulture),
            "Y",
            value.Months.ToString(CultureInfo.InvariantCulture),
            "M");
    }

    private static IntervalYearMonth Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new IntervalYearMonth(0, 0);
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("P", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(1);
        }

        var years = 0;
        var months = 0;
        var buffer = string.Empty;

        foreach (var c in trimmed)
        {
            if (char.IsDigit(c) || c == '-' || c == '+')
            {
                buffer += c;
                continue;
            }

            if (buffer.Length == 0)
            {
                continue;
            }

            switch (c)
            {
                case 'Y':
                    years = int.Parse(buffer, CultureInfo.InvariantCulture);
                    break;
                case 'M':
                    months = int.Parse(buffer, CultureInfo.InvariantCulture);
                    break;
            }

            buffer = string.Empty;
        }

        return new IntervalYearMonth(years, months);
    }
}
