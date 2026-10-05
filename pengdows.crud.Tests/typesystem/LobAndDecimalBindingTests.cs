using System;
using System.Data;
using System.IO;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// Found by TypeCompletenessTests. DbTypeValidator matched a value's exact runtime type, so a
/// MemoryStream or FileStream declared DbType.Binary and any TextReader declared DbType.String were
/// refused, and a gateway over an entity with such a property failed to build its templates at all.
/// SQLite's exact-double check cast the double back to decimal, which overflows at decimal.MaxValue.
/// </summary>
public class LobAndDecimalBindingTests
{
    [Table("lobs")]
    public class Lobs
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("bytes", DbType.Binary)] public Stream? Bytes { get; set; }
        [Column("text", DbType.String)] public TextReader? Text { get; set; }
        [Column("ansi", DbType.AnsiString)] public TextReader? Ansi { get; set; }
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.MySql)]
    public void StreamAndTextReaderProperties_BindAsTheirDeclaredType(SupportedDatabase product)
    {
        using var context = new DatabaseContext($"Data Source=t;EmulatedProduct={product}", new fakeDbFactory(product));
        var gateway = new TableGateway<Lobs, int>(context);

        using var sc = gateway.BuildCreate(new Lobs
        {
            Id = 1, Bytes = new MemoryStream(new byte[] { 1, 2 }), Text = new StringReader("x"), Ansi = new StringReader("y")
        });

        Assert.Contains("INSERT", sc.Query.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_AcceptsStreamAndReaderSubclasses_OnlyForTheirDbTypes()
    {
        Assert.True(DbTypeValidator.IsCompatible(DbType.Binary, new MemoryStream()));
        Assert.True(DbTypeValidator.IsCompatible(DbType.String, new StringReader("x")));
        Assert.True(DbTypeValidator.IsCompatible(DbType.AnsiString, new StringReader("x")));
        Assert.False(DbTypeValidator.IsCompatible(DbType.Int32, new MemoryStream()));
        Assert.False(DbTypeValidator.IsCompatible(DbType.Binary, new StringReader("x")));
        Assert.False(DbTypeValidator.IsCompatible(DbType.String, new MemoryStream()));
    }

    // A stream or reader binds the same whether its static type is Stream/TextReader or object (as a
    // gateway passes it): the dialect's Stream/TextReader mapping (SQL Server's MAX size, Oracle's
    // BLOB/CLOB) applies to a subclass, which missed it and was bound without it.
    [Fact]
    public void StreamAndReader_BindTheSame_TypedOrAsObject()
    {
        var drifted = new System.Collections.Generic.List<string>();
        foreach (var product in Enum.GetValues<SupportedDatabase>())
        {
            if (product == SupportedDatabase.Unknown)
            {
                continue;
            }

            var dialect = (pengdows.crud.dialects.SqlDialect)pengdows.crud.dialects.SqlDialectFactory.CreateDialectForType(product, new fakeDbFactory(product),
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            string Show(Func<System.Data.Common.DbParameter> create)
            {
                try
                {
                    var p = create();
                    var value = p.Value switch
                    {
                        Stream st => "Stream:" + st.GetType().Name,
                        TextReader r => "Reader:" + r.GetType().Name,
                        var v => TypeSystemCharacterizationTests.Show(v)
                    };
                    return p.DbType + " " + value;
                }
                catch (Exception ex)
                {
                    return "throws " + ex.GetType().Name;
                }
            }

            var typedStream = Show(() => dialect.CreateDbParameter<Stream>("p", DbType.Binary, new MemoryStream(new byte[] { 1 })));
            var objectStream = Show(() => dialect.CreateDbParameter<object>("p", DbType.Binary, new MemoryStream(new byte[] { 1 })));
            var typedReader = Show(() => dialect.CreateDbParameter<TextReader>("p", DbType.String, new StringReader("x")));
            var objectReader = Show(() => dialect.CreateDbParameter<object>("p", DbType.String, new StringReader("x")));
            if (typedStream != objectStream)
            {
                drifted.Add($"{product}: Stream typed {typedStream}, as object {objectStream}");
            }

            if (typedReader != objectReader)
            {
                drifted.Add($"{product}: TextReader typed {typedReader}, as object {objectReader}");
            }

            // The driver gets the bytes and the text, the forms every provider binds (live-verified
            // through PortableAdvancedTypeRoundTripTests); no provider is handed a raw stream.
            foreach (var bound in new[] { typedStream, objectStream, typedReader, objectReader })
            {
                if (bound.Contains("Stream:", StringComparison.Ordinal) || bound.Contains("Reader:", StringComparison.Ordinal))
                {
                    drifted.Add($"{product}: bound {bound}");
                }
            }
        }

        Assert.True(drifted.Count == 0, string.Join(Environment.NewLine, drifted));
    }

    [Theory]
    [InlineData("79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335")]
    public void Sqlite_ExtremeDecimal_BindsAsExactText(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        using var context = new DatabaseContext("Data Source=t;EmulatedProduct=Sqlite", new fakeDbFactory(SupportedDatabase.Sqlite));

        var parameter = context.CreateDbParameter("p", DbType.Decimal, value);

        Assert.Equal(text, parameter.Value);
    }
}
