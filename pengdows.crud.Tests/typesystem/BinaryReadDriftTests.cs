using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// DRY-014: UnresolvedColumnReader and ProviderValueFieldReader kept their own copies of
/// TypeCoercionHelper's binary reads. The ReadBytes copy kept the reported length when the provider
/// delivered fewer bytes, so the value carried trailing zeros; the Guid copy failed with
/// InvalidCastException where every other Guid read fails with InvalidValueException.
/// </summary>
public class BinaryReadDriftTests
{
    private static readonly byte[] Wkb = Convert.FromHexString("0101000000000000000000F03F0000000000000040");

    // Reports more bytes than it delivers.
    private static IDataRecord ShortRecord(byte[] delivered, long reported)
    {
        var record = new Mock<IDataRecord>();
        record.Setup(r => r.GetBytes(0, 0, null, 0, 0)).Returns(reported);
        record.Setup(r => r.GetBytes(0, It.IsAny<long>(), It.IsNotNull<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((int _, long offset, byte[] buffer, int at, int length) =>
            {
                var available = Math.Max(0, delivered.Length - (int)offset);
                var count = Math.Min(available, length);
                Array.Copy(delivered, offset, buffer, at, count);
                return count;
            });
        return record.Object;
    }

    [Fact]
    public void ShortBinaryRead_KeepsOnlyTheDeliveredBytes()
    {
        var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.PostgreSql,
            new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger.Instance);

        var geometry = (Geometry)dialect.ReadUnresolvedColumn(ShortRecord(Wkb, Wkb.Length + 4), 0, typeof(Geometry));

        Assert.Equal(Convert.ToHexString(Wkb), Convert.ToHexString(geometry.WellKnownBinary.Span));
    }

    [Fact]
    public void GuidFromWrongLengthBinary_FailsLikeEveryGuidRead()
    {
        var record = new Mock<IDataRecord>();
        record.Setup(r => r.IsDBNull(0)).Returns(false);
        record.Setup(r => r.GetValue(0)).Throws<InvalidCastException>();
        record.Setup(r => r.GetBytes(0, 0, null, 0, 0)).Returns(15);

        Assert.Throws<pengdows.crud.exceptions.InvalidValueException>(() =>
            new ProviderValueFieldReader.GuidColumnReader(false).Read(record.Object, 0));
        Assert.Throws<pengdows.crud.exceptions.InvalidValueException>(() =>
            TypeCoercionHelper.ReadGuidFromBytes(record.Object, 0, false));
    }
}
