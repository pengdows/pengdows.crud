using System;
using System.Buffers.Binary;
using System.Data;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// SQLite has no array type. An int array is bound as its comma-separated text, which the int-array coercion reads back;
/// a float vector is bound as its little-endian float32 bytes, which the packed-vector coercion reads back.
/// </summary>
public class SqliteArrayMappingTests
{
    [Fact]
    public void AnIntArrayIsBoundAsItsCommaSeparatedText()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.Sqlite,
            new fakeDbFactory(SupportedDatabase.Sqlite), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var parameter = dialect.CreateDbParameter("p", DbType.Object, new[] { 1, 2, 3 });

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("1,2,3", parameter.Value);
        Assert.True(new IntArrayCoercion().TryRead(new DbValue("1,2,3"), out var back));
        Assert.Equal(new[] { 1, 2, 3 }, back);
    }

    [Fact]
    public void AFloatVectorIsBoundAsItsFloat32Bytes()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.Sqlite,
            new fakeDbFactory(SupportedDatabase.Sqlite), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var vector = new float[] { 1f, 2.5f, 3f };

        var parameter = dialect.CreateDbParameter("p", DbType.Object, vector);

        Assert.Equal(DbType.Binary, parameter.DbType);
        var bytes = Assert.IsType<byte[]>(parameter.Value);
        Assert.Equal(12, bytes.Length);
        Assert.Equal(1f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(0)));
        Assert.Equal(2.5f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(4)));
        Assert.Equal(3f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(8)));
    }
}
