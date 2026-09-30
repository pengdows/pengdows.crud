using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-003, found live 2026-09-30 on SQLite: a char property was passed to the provider as a raw
/// System.Char, which Microsoft.Data.Sqlite stored as an empty string ('~' came back ""), and reading
/// "" into a char threw NullReferenceException from the compiled mapper. A char bound as a string
/// DbType is now sent as its one-character string on every dialect, and a stored text value that
/// isn't exactly one character fails as DataMappingException.
/// </summary>
public sealed class CharMappingTests
{
    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    public void CharBoundAsAStringType_IsSentAsAOneCharacterString(SupportedDatabase database)
    {
        var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database),
            NullLogger<SqlDialect>.Instance);

        foreach (var type in new[] { DbType.StringFixedLength, DbType.String, DbType.AnsiStringFixedLength })
        {
            var parameter = dialect.CreateDbParameter("p", type, '~');
            Assert.Equal("~", parameter.Value);
        }
    }

    [Theory]
    [InlineData("~", false)]
    [InlineData("", true)]
    [InlineData("ab", true)]
    public async Task ReadingTextIntoAChar_IsExactOrADataMappingException(string stored, bool fails)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=chars;EmulatedProduct=Sqlite", factory);
        factory.EnqueueReaderResult(new[] { new Dictionary<string, object> { ["id"] = 1L, ["code"] = stored } });
        var gateway = new TableGateway<Row, int>(context);

        if (fails)
        {
            var ex = await Assert.ThrowsAsync<DataMappingException>(async () => await gateway.RetrieveOneAsync(1));
            Assert.Contains("code", ex.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal('~', (await gateway.RetrieveOneAsync(1))!.Code);
        }
    }

    [Table("chars")]
    private sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("code", DbType.StringFixedLength)] public char Code { get; set; }
    }
}
