using System;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

/// <summary>
/// Converts between database row version/timestamp values and <see cref="RowVersion"/> value objects.
/// Provides optimistic concurrency control through automatic version tracking.
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific behavior:</strong></para>
/// <list type="bullet">
/// <item><description><strong>SQL Server:</strong> Maps to ROWVERSION or TIMESTAMP columns (8-byte auto-incrementing binary). Database manages values automatically.</description></item>
/// <item><description><strong>PostgreSQL:</strong> Maps to BYTEA columns with application-managed versioning, or use xmin system column for database-managed versioning.</description></item>
/// <item><description><strong>Oracle:</strong> Maps to RAW columns. Typically application-managed.</description></item>
/// <item><description><strong>MySQL:</strong> No native row version type. Use BINARY or application-managed integer version.</description></item>
/// <item><description><strong>SQLite:</strong> No native row version type. Use BLOB or application-managed integer version.</description></item>
/// </list>
/// <para><strong>Supported conversions from database:</strong></para>
/// <list type="bullet">
/// <item><description>RowVersion → RowVersion (pass-through)</description></item>
/// <item><description>byte[] → RowVersion (via RowVersion.FromBytes)</description></item>
/// <item><description>ReadOnlyMemory&lt;byte&gt; → RowVersion</description></item>
/// <item><description>ArraySegment&lt;byte&gt; → RowVersion</description></item>
/// </list>
/// <para><strong>Concurrency control:</strong> When used with the [Version] attribute, pengdows.crud automatically
/// includes the row version in WHERE clauses during UPDATE operations, preventing lost updates.</para>
/// <para><strong>Thread safety:</strong> Converter instances are thread-safe. RowVersion value objects are immutable and thread-safe.</para>
/// </remarks>
/// <example>
/// <code>
/// // Entity with optimistic locking
/// [Table("accounts")]
/// public class Account
/// {
///     [Id]
///     [Column("id", DbType.Int32)]
///     public int Id { get; set; }
///
///     [Column("balance", DbType.Decimal)]
///     public decimal Balance { get; set; }
///
///     [Version]
///     [Column("row_version", DbType.Binary)]
///     public RowVersion Version { get; set; }
/// }
///
/// // Concurrency protection in action
/// var account = await helper.RetrieveOneAsync(accountId);  // Version = current
///
/// // Another user updates the account
/// // Version changes in database
///
/// // Try to update with stale version
/// account.Balance += 100;
/// var rowsAffected = await helper.UpdateAsync(account);
///
/// if (rowsAffected == 0)
/// {
///     // Concurrency conflict detected!
///     throw new DbUpdateConcurrencyException("Account was modified by another user");
/// }
/// </code>
/// </example>
internal sealed class RowVersionConverter : AdvancedTypeConverter<RowVersion>
{
    protected override object? ConvertToProvider(RowVersion value, SupportedDatabase provider)
    {
        return value.ToArray();
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out RowVersion result)
    {
        switch (value)
        {
            case RowVersion rv:
                result = rv;
                return true;
            case byte[] bytes:
                try
                {
                    result = RowVersion.FromBytes(bytes);
                    return true;
                }
                catch
                {
                    result = default!;
                    return false;
                }
            case ReadOnlyMemory<byte> memory:
                try
                {
                    result = RowVersion.FromBytes(memory.ToArray());
                    return true;
                }
                catch
                {
                    result = default!;
                    return false;
                }
            case ArraySegment<byte> segment:
                try
                {
                    result = RowVersion.FromBytes(segment.ToArray());
                    return true;
                }
                catch
                {
                    result = default!;
                    return false;
                }
            default:
                result = default!;
                return false;
        }
    }
}