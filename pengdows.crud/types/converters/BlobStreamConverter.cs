using pengdows.crud.enums;

namespace pengdows.crud.types.converters;

/// <summary>
/// Converts between database Binary Large Object (BLOB) values and <see cref="Stream"/> instances.
/// Supports memory-efficient streaming of binary data without loading entire contents into memory.
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific behavior:</strong></para>
/// <list type="bullet">
/// <item><description><strong>SQL Server:</strong> Maps to VARBINARY(MAX), IMAGE columns. Provider returns byte[] for small values, Stream for large values.</description></item>
/// <item><description><strong>PostgreSQL:</strong> Maps to BYTEA columns. Provider typically returns byte[].</description></item>
/// <item><description><strong>Oracle:</strong> Maps to BLOB columns. Provider returns Oracle-specific blob stream.</description></item>
/// <item><description><strong>MySQL:</strong> Maps to BLOB, LONGBLOB columns. Provider returns byte[] or Stream.</description></item>
/// <item><description><strong>SQLite:</strong> Maps to BLOB type. Provider returns byte[].</description></item>
/// </list>
/// <para><strong>Supported conversions from database:</strong></para>
/// <list type="bullet">
/// <item><description>Stream → Stream (pass-through, seeks to beginning if seekable)</description></item>
/// <item><description>byte[] → MemoryStream (read-only, zero-copy when possible)</description></item>
/// <item><description>ReadOnlyMemory&lt;byte&gt; → MemoryStream (read-only)</description></item>
/// <item><description>ArraySegment&lt;byte&gt; → MemoryStream (read-only, uses offset/count)</description></item>
/// </list>
/// <para><strong>Memory efficiency:</strong> When the provider returns a Stream (large BLOBs), no buffering occurs.
/// When the provider returns byte[], a MemoryStream wraps the array without copying.</para>
/// <para><strong>Thread safety:</strong> Converter instances are thread-safe. Returned streams are NOT thread-safe
/// and should not be shared across threads.</para>
/// </remarks>
/// <example>
/// <code>
/// // Entity with BLOB column
/// [Table("documents")]
/// public class Document
/// {
///     [Id]
///     [Column("id", DbType.Int32)]
///     public int Id { get; set; }
///
///     [Column("content", DbType.Object)]
///     public Stream Content { get; set; }
/// }
///
/// // Write large file without loading into memory
/// await using var fileStream = File.OpenRead("large-file.pdf");
/// var doc = new Document { Content = fileStream };
/// await helper.CreateAsync(doc);
///
/// // Read large BLOB as stream
/// var retrieved = await helper.RetrieveOneAsync(doc.Id);
/// await using var content = retrieved.Content;
/// await content.CopyToAsync(outputStream);
/// </code>
/// </example>
internal sealed class BlobStreamConverter : AdvancedTypeConverter<Stream>
{
    protected override object? ConvertToProvider(Stream value, SupportedDatabase provider)
    {
        if (value.CanSeek)
        {
            value.Seek(0, SeekOrigin.Begin);
        }

        return value;
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out Stream result)
    {
        switch (value)
        {
            case Stream stream:
                try
                {
                    if (stream.CanSeek)
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                    }

                    result = stream;
                    return true;
                }
                catch
                {
                    result = default!;
                    return false;
                }
            case byte[] bytes:
                result = new MemoryStream(bytes, false);
                return true;
            case ReadOnlyMemory<byte> memory:
                result = new MemoryStream(memory.ToArray(), false);
                return true;
            case ArraySegment<byte> segment:
                if (segment.Array != null)
                {
                    result = new MemoryStream(segment.Array, segment.Offset, segment.Count, false);
                    return true;
                }

                result = default!;
                return false;
            default:
                result = default!;
                return false;
        }
    }
}