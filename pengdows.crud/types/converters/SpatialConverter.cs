using System;
using System.Text;
using pengdows.crud.enums;
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
/// (EWKB for PostgreSQL, provider types for SQL Server/Oracle, WKB for MySQL).</para>
/// <para><strong>SRID handling:</strong> Spatial Reference System Identifier specifies coordinate system.
/// Default is 0 (unspecified). Common: 4326 (WGS84 lat/lon for GPS), 3857 (Web Mercator).</para>
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
            SupportedDatabase.Oracle => value.ProviderValue ?? throw new InvalidOperationException("Oracle spatial parameters require provider-specific objects. Use WithProviderValue to supply SDO_GEOMETRY."),
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
            throw new InvalidOperationException("Microsoft.SqlServer.Types is required for SQL Server spatial parameters. Reference the package or provide a provider-specific instance.");
        }

        var methodName = !value.WellKnownBinary.IsEmpty ? "STGeomFromWKB" : "STGeomFromText";
        if (targetType == sqlGeographyType)
        {
            methodName = !value.WellKnownBinary.IsEmpty ? "STGeomFromWKB" : "STGeomFromText";
        }

        var sqlBytesType = Type.GetType("Microsoft.SqlServer.Types.SqlBytes, Microsoft.SqlServer.Types");
        var sqlCharsType = Type.GetType("Microsoft.SqlServer.Types.SqlChars, Microsoft.SqlServer.Types");
        var sqlStringType = Type.GetType("System.Data.SqlTypes.SqlString, System.Data");
        var sqlIntType = Type.GetType("System.Data.SqlTypes.SqlInt32, System.Data");

        if (sqlBytesType == null || sqlCharsType == null || sqlStringType == null || sqlIntType == null)
        {
            throw new InvalidOperationException("SQL Server spatial conversion requires System.Data.SqlTypes and Microsoft.SqlServer.Types assemblies.");
        }

        if (!value.WellKnownBinary.IsEmpty)
        {
            var ctor = sqlBytesType.GetConstructor(new[] { typeof(byte[]) });
            var sqlBytes = ctor?.Invoke(new object[] { value.WellKnownBinary.ToArray() });
            var srid = Activator.CreateInstance(sqlIntType, value.Srid);
            return targetType.GetMethod(methodName, new[] { sqlBytesType, sqlIntType })?.Invoke(null, new[] { sqlBytes!, srid! });
        }

        var text = value.WellKnownText ?? value.GeoJson;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Spatial value must contain WKB or WKT to build SQL Server UDT.");
        }

        var sqlCharsCtor = sqlCharsType.GetConstructor(new[] { typeof(char[]) });
        var sqlChars = sqlCharsCtor?.Invoke(new object[] { text.ToCharArray() });
        var sridValue = Activator.CreateInstance(sqlIntType, value.Srid);

        return targetType.GetMethod(methodName, new[] { sqlCharsType, sqlIntType })?.Invoke(null, new[] { sqlChars!, sridValue! });
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
            var method = type.GetMethod("STAsBinary", Type.EmptyTypes);
            if (method != null)
            {
                if (method.Invoke(value, Array.Empty<object>()) is byte[] bytes)
                {
                    var sridProp = type.GetProperty("STSrid");
                    var sridValue = sridProp?.GetValue(value);
                    var srid = sridValue != null ? Convert.ToInt32(sridValue, System.Globalization.CultureInfo.InvariantCulture) : 4326;
                    var spatial = FromBinary(bytes, provider);
                    return WrapWithProvider(spatial, value);
                }
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
