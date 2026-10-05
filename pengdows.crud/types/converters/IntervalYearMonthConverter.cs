// =============================================================================
// FILE: IntervalYearMonthConverter.cs
// PURPOSE: Converter for Oracle INTERVAL YEAR TO MONTH type.
//
// AI SUMMARY:
// - Converts between database interval values and IntervalYearMonth value objects.
// - Represents intervals with Years and Months components only (no days/time).
// - Provider-specific:
//   * Oracle: INTERVAL YEAR TO MONTH type
//   * PostgreSQL/CockroachDB: INTERVAL (ISO 8601 format)
//   * Others: Raw value
// - ConvertToProvider(): Returns ISO 8601 format (P3Y6M) for Oracle/PostgreSQL.
// - TryConvertFromProvider(): Handles IntervalYearMonth, a month count (int/long/short) and text.
// - Text is read by IntervalYearMonth.TryParseText (ISO, SQL/Oracle/Informix and word forms; DRY-012).
// - Use case: Date arithmetic where month boundaries matter.
// - Thread-safe and immutable value objects.
// =============================================================================

using System.Globalization;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

/// <summary>
/// Converts between database interval year-to-month values and <see cref="IntervalYearMonth"/> value objects.
/// Represents Oracle INTERVAL YEAR TO MONTH type with years and months components.
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific behavior:</strong></para>
/// <list type="bullet">
/// <item><description><strong>Oracle:</strong> Maps to INTERVAL YEAR TO MONTH type. Written as Oracle literal +YYYY-MM (e.g. +0003-06).</description></item>
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
        return DatabaseTraits.For(provider).IntervalFormat switch
        {
            IntervalWireFormat.OracleLiteral => FormatOracle(value),
            IntervalWireFormat.Iso8601 => FormatIso(value),
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

        // A month count (Informix, Db2 and some drivers return one; DRY-010).
        if (value is int or long or short)
        {
            try
            {
                result = IntervalYearMonth.FromTotalMonths(checked((int)Convert.ToInt64(value, CultureInfo.InvariantCulture)));
                return true;
            }
            catch (OverflowException)
            {
                result = default!;
                return false;
            }
        }

        // DRY-012: the value object's strict parser; blank or unrecognized text is no interval.
        if (value is string text)
        {
            return IntervalYearMonth.TryParseText(text, out result);
        }

        result = default!;
        return false;
    }

    // ODP.NET binds INTERVAL YEAR TO MONTH from Oracle's literal format (+YYYY-MM), not ISO-8601.
    private static string FormatOracle(IntervalYearMonth value)
    {
        var sign = value.TotalMonths < 0 ? "-" : "+";
        var absoluteMonths = Math.Abs(value.TotalMonths);
        return string.Concat(sign,
            (absoluteMonths / 12).ToString("D4", CultureInfo.InvariantCulture), "-",
            (absoluteMonths % 12).ToString("D2", CultureInfo.InvariantCulture));
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
}