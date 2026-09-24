// =============================================================================
// FILE: SpatialConverter.cs
// PURPOSE: Base class for spatial type converters (Geometry and Geography).
//
// AI SUMMARY:
// - Abstract base for GeometryConverter and GeographyConverter.
// - Supports WKB/EWKB, WKT/EWKT, and GeoJSON formats with SRID handling.
// - ConvertToProvider(): Creates provider-specific spatial objects:
//   * SQL Server: SqlGeometry/SqlGeography via reflection
//   * PostgreSQL/CockroachDB: byte[] (stored WKB/EWKB bytes) or string (WKT/GeoJSON)
//   * MySQL: byte[] (WKB) or UTF-8 encoded WKT
//   * Oracle: Requires ProviderValue to be set with SDO_GEOMETRY
// - TryConvertFromProvider(): Converts database values back to TSpatial:
//   * byte[]/ReadOnlyMemory<byte>/ArraySegment<byte> -> WKB parsing
//   * string -> WKT or GeoJSON (auto-detected by leading '{')
//   * Provider-specific types via reflection (SqlGeometry, NpgsqlTypes)
// - Abstract methods for subclasses:
//   * FromBinary(), FromTextInternal(), FromGeoJsonInternal(), WrapWithProvider()
// - Thread-safe: Converter instances and spatial value objects are immutable.
// =============================================================================

using System.Data.SqlTypes;
using System.Text;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.converters;

/// <summary>
/// Base converter for spatial data types supporting Well-Known Binary (WKB), Well-Known Text (WKT), and GeoJSON formats.
/// Provides cross-database spatial type conversion with SRID (Spatial Reference Identifier) support.
/// </summary>
/// <typeparam name="TSpatial">The spatial value object type (Geometry or Geography).</typeparam>
/// <remarks>
/// <para><strong>Provider-specific behavior:</strong></para>
/// <list type="bullet">
/// <item><description><strong>SQL Server:</strong> Uses Microsoft.SqlServer.Types (SqlGeometry/SqlGeography). Supports WKB, WKT, SRID.</description></item>
/// <item><description><strong>PostgreSQL:</strong> Uses PostGIS extension. Supports EWKB (Extended WKB with SRID), EWKT, WKB, WKT. Requires PostGIS installed.</description></item>
/// <item><description><strong>CockroachDB:</strong> PostGIS-compatible spatial types.</description></item>
/// <item><description><strong>MySQL:</strong> Uses native spatial types (GEOMETRY, POINT, etc.) with WKB format.</description></item>
/// <item><description><strong>Oracle:</strong> Uses SDO_GEOMETRY type. Requires provider-specific objects via WithProviderValue().</description></item>
/// </list>
/// <para><strong>Supported input formats from database:</strong></para>
/// <list type="bullet">
/// <item><description>byte[] → WKB (Well-Known Binary) or EWKB (Extended WKB with SRID prefix)</description></item>
/// <item><description>string → WKT (Well-Known Text) like "POINT(1 2)" or EWKT like "SRID=4326;POINT(1 2)"</description></item>
/// <item><description>Provider-specific types → Automatic detection and conversion (SqlGeometry, PostGIS types, etc.)</description></item>
/// </list>
/// <para><strong>Output formats to database:</strong> Automatically selects optimal format per provider
/// (stored WKB bytes for PostgreSQL, provider types for SQL Server/Oracle, WKB for MySQL).</para>
/// <para><strong>SRID handling:</strong> Spatial Reference System Identifier specifies coordinate system.
/// Default is 0 (unspecified) for Geometry; GeographyConverter defaults to 4326 on read. Common: 4326 (WGS84 lat/lon for GPS), 3857 (Web Mercator).</para>
/// <para><strong>Thread safety:</strong> Converter instances are thread-safe. Spatial value objects are immutable and thread-safe.</para>
/// </remarks>
internal abstract class SpatialConverter<TSpatial> : AdvancedTypeConverter<TSpatial>
    where TSpatial : SpatialValue
{
    protected override object? ConvertToProvider(TSpatial value, SupportedDatabase provider)
    {
        if (value.ProviderValue != null)
        {
            return value.ProviderValue;
        }

        return provider switch
        {
            SupportedDatabase.SqlServer => CreateSqlServerSpatial(value),
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb => CreatePostgresSpatial(value),
            SupportedDatabase.MySql or SupportedDatabase.MariaDb => CreateMySqlSpatial(value),
            SupportedDatabase.Oracle => value.ProviderValue ?? throw new InvalidOperationException(
                "Oracle spatial parameters require provider-specific objects. Use WithProviderValue to supply SDO_GEOMETRY."),
            _ => ExtractDefaultSpatial(value)
        };
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out TSpatial result)
    {
        try
        {
            switch (value)
            {
                case byte[] bytes:
                    result = FromWellKnownBinary(bytes, provider);
                    return true;
                case ReadOnlyMemory<byte> memory:
                    result = FromWellKnownBinary(memory.ToArray(), provider);
                    return true;
                case ArraySegment<byte> segment:
                    result = FromWellKnownBinary(segment.ToArray(), provider);
                    return true;
                case string text:
                    result = FromText(text, provider);
                    return true;
                default:
                    var specific = FromProviderSpecific(value, provider);
                    if (specific != null)
                    {
                        result = specific;
                        return true;
                    }

                    result = default!;
                    return false;
            }
        }
        catch
        {
            result = default!;
            return false;
        }
    }

    protected abstract TSpatial FromBinary(ReadOnlySpan<byte> wkb, SupportedDatabase provider);

    /// <summary>Builds the value from WKB with an explicit SRID (e.g. read from a provider type).</summary>
    protected abstract TSpatial FromBinaryWithSrid(ReadOnlySpan<byte> wkb, int srid, object providerValue);
    protected abstract TSpatial FromTextInternal(string text, SupportedDatabase provider);
    protected abstract TSpatial FromGeoJsonInternal(string json, SupportedDatabase provider);
    protected abstract TSpatial WrapWithProvider(TSpatial spatial, object providerValue);

    private object? CreateSqlServerSpatial(SpatialValue value)
    {
        var sqlGeometryType = Type.GetType("Microsoft.SqlServer.Types.SqlGeometry, Microsoft.SqlServer.Types");
        var sqlGeographyType = Type.GetType("Microsoft.SqlServer.Types.SqlGeography, Microsoft.SqlServer.Types");

        var targetType = typeof(Geometry).IsAssignableFrom(value.GetType()) ? sqlGeometryType : sqlGeographyType;
        if (targetType == null)
        {
            throw new InvalidOperationException(
                "Microsoft.SqlServer.Types is required for SQL Server spatial parameters. Reference the package or provide a provider-specific instance.");
        }

        // Microsoft.SqlServer.Types: STGeomFromWKB(SqlBytes, int srid) / STGeomFromText(SqlChars, int srid)
        // on both SqlGeometry and SqlGeography. SqlBytes/SqlChars are System.Data.SqlTypes (BCL).
        if (!value.WellKnownBinary.IsEmpty)
        {
            return targetType.GetMethod("STGeomFromWKB", new[] { typeof(SqlBytes), typeof(int) })
                !.Invoke(null, new object[] { new SqlBytes(value.WellKnownBinary.ToArray()), value.Srid });
        }

        // SQL Server only accepts WKB or WKT; GeoJSON is not WKT and would fail to parse.
        if (string.IsNullOrWhiteSpace(value.WellKnownText))
        {
            throw new InvalidOperationException(
                "SQL Server spatial parameters require WKB or WKT; this value has neither (GeoJSON is not supported for SQL Server).");
        }

        return targetType.GetMethod("STGeomFromText", new[] { typeof(SqlChars), typeof(int) })
            !.Invoke(null, new object[] { new SqlChars(value.WellKnownText.ToCharArray()), value.Srid });
    }

    private object? CreatePostgresSpatial(SpatialValue value)
    {
        if (!value.WellKnownBinary.IsEmpty)
        {
            return value.WellKnownBinary.ToArray();
        }

        if (!string.IsNullOrEmpty(value.WellKnownText))
        {
            return value.WellKnownText;
        }

        if (!string.IsNullOrEmpty(value.GeoJson))
        {
            return value.GeoJson;
        }

        throw new InvalidOperationException("Spatial value did not contain WKB, WKT, or GeoJSON data.");
    }

    private object? CreateMySqlSpatial(SpatialValue value)
    {
        if (!value.WellKnownBinary.IsEmpty)
        {
            return value.WellKnownBinary.ToArray();
        }

        if (!string.IsNullOrEmpty(value.WellKnownText))
        {
            return Encoding.UTF8.GetBytes(value.WellKnownText);
        }

        throw new InvalidOperationException("MySQL spatial values require WKB or WKT data.");
    }

    private object? ExtractDefaultSpatial(SpatialValue value)
    {
        if (!value.WellKnownBinary.IsEmpty)
        {
            return value.WellKnownBinary.ToArray();
        }

        if (!string.IsNullOrEmpty(value.WellKnownText))
        {
            return value.WellKnownText;
        }

        return value.GeoJson;
    }

    private TSpatial FromWellKnownBinary(byte[] bytes, SupportedDatabase provider)
    {
        return FromBinary(bytes, provider);
    }

    private TSpatial FromText(string text, SupportedDatabase provider)
    {
        if (text.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            return FromGeoJsonInternal(text, provider);
        }

        return FromTextInternal(text, provider);
    }

    private TSpatial? FromProviderSpecific(object value, SupportedDatabase provider)
    {
        var type = value.GetType();
        var typeName = type.FullName ?? string.Empty;

        if (typeName.Contains("SqlGeometry", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("SqlGeography", StringComparison.OrdinalIgnoreCase))
        {
            // STAsBinary() returns SqlBytes and STSrid is a SqlInt32 (Microsoft.SqlServer.Types).
            var binary = type.GetMethod("STAsBinary", Type.EmptyTypes)?.Invoke(value, Array.Empty<object>());
            var bytes = binary switch
            {
                SqlBytes { IsNull: false } sqlBytes => sqlBytes.Value,
                byte[] raw => raw,
                _ => null
            };

            if (bytes != null)
            {
                var srid = type.GetProperty("STSrid")?.GetValue(value) is SqlInt32 { IsNull: false } sqlSrid
                    ? sqlSrid.Value
                    : 0;
                return FromBinaryWithSrid(bytes, srid, value);
            }
        }

        if (typeName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            var bytesProp = type.GetProperty("AsBinary");
            if (bytesProp?.GetValue(value) is byte[] data)
            {
                var spatial = FromBinary(data, provider);
                return WrapWithProvider(spatial, value);
            }
        }

        if (value is ReadOnlyMemory<byte> memory)
        {
            return FromBinary(memory.ToArray(), provider);
        }

        throw new NotSupportedException($"Unsupported spatial provider type: {type.FullName}");
    }
}