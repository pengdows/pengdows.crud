using System;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// A column the driver returns as a Guid (Firebird's CHAR(16) OCTETS, written big-endian) reads into a byte[] as the bytes
/// the dialect stored: the inverse of the Guid it read them as, in the dialect's byte order.
/// </summary>
public class GuidAsBytesTests
{
    [Fact]
    public void BigEndianGuidReadsBackTheStoredBytes()
    {
        var stored = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var guid = new Guid(stored, bigEndian: true);
        var options = pengdows.crud.TypeCoercionOptions.For(
            pengdows.crud.dialects.SqlDialectFactory.CreateDialectForType(SupportedDatabase.Firebird,
                new fakeDbFactory(SupportedDatabase.Firebird), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));

        var bytes = pengdows.crud.TypeCoercionHelper.Coerce(guid, typeof(Guid), typeof(byte[]), options);

        Assert.Equal(stored, bytes);
    }
}
