using System;
using System.Data;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// SingleStore has no GEOMETRY type, and its GEOGRAPHY is WGS 84 only: a geometry's SRID cannot be stored there. The library
/// refuses a geometry loudly rather than store its text and lose the SRID. A geography (SRID 4326) is stored as its text.
/// </summary>
public class SingleStoreGeometryRefusalTests
{
    [Fact]
    public void AGeometryIsRefusedNotStoredWithoutItsSrid()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.SingleStore,
            new fakeDbFactory(SupportedDatabase.SingleStore), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var ex = Assert.Throws<NotSupportedException>(() =>
            dialect.CreateDbParameter("p", DbType.Object, Geometry.FromWellKnownText("POINT (1 2)", 4326)));

        Assert.Contains("GEOMETRY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AGeographyIsStillStoredAsItsText()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.SingleStore,
            new fakeDbFactory(SupportedDatabase.SingleStore), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var parameter = dialect.CreateDbParameter("p", DbType.Object, Geography.FromWellKnownText("POINT (1 2)", 4326));

        Assert.Equal("POINT (1 2)", parameter.Value);
    }
}
