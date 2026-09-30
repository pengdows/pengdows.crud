using System.Data;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// TYPE-004, found live 2026-09-30: Informix.Net.Core's IfxParameter rejects DbType.Object ("No
/// mapping exists from DbType Object to a known IfxType"), so an entity with a Stream or TextReader
/// property declared DbType.Object could not build its SQL templates. Informix leaves that DbType
/// unset (the driver infers it from the value) and materializes streams/readers to byte[]/string.
/// </summary>
public sealed class InformixObjectDbTypeTests
{
    private static SqlDialect Dialect(SupportedDatabase database) =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database),
            NullLogger<SqlDialect>.Instance);

    [Fact]
    public void Informix_NeverAssignsObjectDbType()
    {
        var parameter = Dialect(SupportedDatabase.Informix).CreateDbParameter<object?>("p", DbType.Object, null);

        Assert.NotEqual(DbType.Object, parameter.DbType);
    }

    [Fact]
    public void Informix_MaterializesStreamsAndReadersForObjectColumns()
    {
        var dialect = Dialect(SupportedDatabase.Informix);

        Assert.Equal(new byte[] { 9, 8, 7 }, dialect.PrepareParameterValue(new MemoryStream(new byte[] { 9, 8, 7 }), DbType.Object));
        Assert.Equal("notes", dialect.PrepareParameterValue(new StringReader("notes"), DbType.Object));
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    public void OtherDialects_StillAssignObjectDbType(SupportedDatabase database)
    {
        Assert.Equal(DbType.Object, Dialect(database).CreateDbParameter<object?>("p", DbType.Object, null).DbType);
    }
}
