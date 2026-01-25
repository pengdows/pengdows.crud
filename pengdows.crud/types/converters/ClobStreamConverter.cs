using System.Text;
using pengdows.crud.enums;

namespace pengdows.crud.types.converters;

/// <summary>
/// Converts between database Character Large Object (CLOB) values and <see cref="TextReader"/> instances.
/// Supports memory-efficient streaming of text data without loading entire contents into memory.
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific behavior:</strong></para>
/// <list type="bullet">
/// <item><description><strong>SQL Server:</strong> Maps to VARCHAR(MAX), NVARCHAR(MAX), TEXT, NTEXT columns. Provider returns string for most values.</description></item>
/// <item><description><strong>PostgreSQL:</strong> Maps to TEXT columns. Provider returns string.</description></item>
/// <item><description><strong>Oracle:</strong> Maps to CLOB, NCLOB columns. Provider returns Oracle-specific clob reader or string.</description></item>
/// <item><description><strong>MySQL:</strong> Maps to TEXT, LONGTEXT columns. Provider returns string or Stream.</description></item>
/// <item><description><strong>SQLite:</strong> Maps to TEXT type. Provider returns string.</description></item>
/// </list>
/// <para><strong>Supported conversions from database:</strong></para>
/// <list type="bullet">
/// <item><description>TextReader → TextReader (pass-through)</description></item>
/// <item><description>string → StringReader (zero-copy wrapper)</description></item>
/// <item><description>Stream → StreamReader (UTF-8 with BOM detection, leaves stream open)</description></item>
/// <item><description>ReadOnlyMemory&lt;char&gt; → StringReader</description></item>
/// </list>
/// <para><strong>Encoding:</strong> When converting from Stream, UTF-8 encoding is assumed with automatic
/// Byte Order Mark (BOM) detection. The stream is left open for proper lifecycle management.</para>
/// <para><strong>Thread safety:</strong> Converter instances are thread-safe. Returned TextReader instances
/// are NOT thread-safe and should not be shared across threads.</para>
/// </remarks>
/// <example>
/// <code>
/// // Entity with CLOB column
/// [Table("articles")]
/// public class Article
/// {
///     [Id]
///     [Column("id", DbType.Int32)]
///     public int Id { get; set; }
///
///     [Column("content", DbType.Object)]
///     public TextReader Content { get; set; }
/// }
///
/// // Write large text without loading into memory
/// using var textReader = File.OpenText("large-article.txt");
/// var article = new Article { Content = textReader };
/// await helper.CreateAsync(article);
///
/// // Read large CLOB as TextReader
/// var retrieved = await helper.RetrieveOneAsync(article.Id);
/// using var content = retrieved.Content;
/// string firstLine = await content.ReadLineAsync();
/// </code>
/// </example>
internal sealed class ClobStreamConverter : AdvancedTypeConverter<TextReader>
{
    protected override object? ConvertToProvider(TextReader value, SupportedDatabase provider)
    {
        return value;
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out TextReader result)
    {
        switch (value)
        {
            case TextReader reader:
                result = reader;
                return true;
            case string text:
                result = new StringReader(text);
                return true;
            case Stream stream:
                try
                {
                    result = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
                    return true;
                }
                catch
                {
                    result = default!;
                    return false;
                }
            case ReadOnlyMemory<char> memory:
                result = new StringReader(memory.ToString());
                return true;
            default:
                result = default!;
                return false;
        }
    }
}