using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
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

    // A provider that reports more bytes than it delivers.
    private static IDataRecord ShortRecord(byte[] delivered, int extraReported)
    {
        var reader = new fakeDbDataReader(new List<Dictionary<string, object>> { new() { ["value"] = delivered } })
        {
            OverstatedLengthColumns = new Dictionary<string, int> { ["value"] = extraReported }
        };
        Assert.True(reader.Read());
        return reader;
    }

    [Fact]
    public void ShortBinaryRead_KeepsOnlyTheDeliveredBytes()
    {
        var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.PostgreSql,
            new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger.Instance);

        var geometry = (Geometry)dialect.ReadUnresolvedColumn(ShortRecord(Wkb, 4), 0, typeof(Geometry));

        Assert.Equal(Convert.ToHexString(Wkb), Convert.ToHexString(geometry.WellKnownBinary.Span));
    }

    [Fact]
    public void GuidFromWrongLengthBinary_FailsLikeEveryGuidRead()
    {
        // GetValue throws InvalidCastException (a column the provider has no handler for) and the binary value is 15 bytes.
        var reader = new fakeDbDataReader(new List<Dictionary<string, object>> { new() { ["value"] = new byte[15] } })
        {
            HandlerlessColumns = new Dictionary<string, string> { ["value"] = "public.guid15" }
        };
        Assert.True(reader.Read());
        IDataRecord record = reader;

        Assert.Throws<pengdows.crud.exceptions.InvalidValueException>(() =>
            new ProviderValueFieldReader.GuidColumnReader(false).Read(record, 0));
        Assert.Throws<pengdows.crud.exceptions.InvalidValueException>(() =>
            TypeCoercionHelper.ReadGuidFromBytes(record, 0, false));
    }
}
