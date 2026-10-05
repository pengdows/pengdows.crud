// =============================================================================
// FILE: SpatialConverter.cs
// PURPOSE: Base class for spatial type converters (Geometry and Geography).
//
// AI SUMMARY:
// - Abstract base for GeometryConverter and GeographyConverter.
// - Supports WKB/EWKB, WKT/EWKT, and GeoJSON formats with SRID handling.
// - ConvertToProvider(): Creates provider-specific spatial objects:
//   * SQL Server: big-endian SRID + WKB, built into the instance server-side (SqlServerSpatialFormat)
//   * PostgreSQL/CockroachDB/YugabyteDB: byte[] (stored WKB/EWKB bytes) or string (WKT/GeoJSON)
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

using System.Buffers.Binary;
using System.Data.SqlTypes;
using System.Text;
using System.Text.Json.Nodes;
using pengdows.crud.dialects;
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
/// <item><description><strong>SQL Server:</strong> No Microsoft.SqlServer.Types needed: written as a big-endian SRID + WKB that the gateway SQL turns into the instance with STGeomFromWKB, read from SQL Server's stored encoding. Supports WKB, WKT, SRID.</description></item>
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
/// (stored WKB bytes for PostgreSQL, SRID-prefixed WKB for SQL Server and MySQL, provider types for Oracle).</para>
/// <para><strong>SRID handling:</strong> Spatial Reference System Identifier specifies coordinate system.
/// Default is 0 (unspecified) for Geometry; GeographyConverter defaults to 4326 on read. Common: 4326 (WGS84 lat/lon for GPS), 3857 (Web Mercator).</para>
/// <para><strong>Thread safety:</strong> Converter instances are thread-safe. Spatial value objects are immutable and thread-safe.</para>
/// </remarks>
internal abstract class SpatialConverter<TSpatial> : AdvancedTypeConverter<TSpatial>
    where TSpatial : SpatialValue
{
    protected override object? ConvertToProvider(TSpatial value, SupportedDatabase provider)
    {
        // SQL Server builds the instance itself from a big-endian SRID + WKB
        // (SqlServerDialect.RenderColumnArgument), including from a SqlGeometry/SqlGeography read
        // with Microsoft.SqlServer.Types loaded, whose WKB and SRID the value already carries.
        var format = DatabaseTraits.For(provider).SpatialFormat;
        if (format == SpatialWireFormat.SridPrefixedWkbForConstructor)
        {
            return SqlServerSpatialFormat.ToConstructorArgument(value);
        }

        if (value.ProviderValue != null)
        {
            return value.ProviderValue;
        }

        // The format is declared by the dialect (DatabaseTraits, REV-039); the error texts name the
        // one database that declares each format today.
        return format switch
        {
            SpatialWireFormat.ExtendedWkb => CreatePostgresSpatial(value),
            SpatialWireFormat.LittleEndianSridPrefixedWkb => CreateMySqlSpatial(value),
            // Plain WKT, a value built from WKB decoded to it.
            SpatialWireFormat.WellKnownText => ExtendedWellKnownText.WellKnownTextOf(value),
            SpatialWireFormat.ExtendedTextOrHex => CreateSnowflakeSpatial(value),
            SpatialWireFormat.PlainWkb => CreateWkb(value, "SAP HANA"),
            // EWKT text, always with its SRID: OracleDialect builds SDO_GEOMETRY from it (TYPE-021).
            SpatialWireFormat.ExtendedWellKnownText => ExtendedWellKnownText.From(value),
            _ => ExtractDefaultSpatial(value)
        };
    }

    public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out TSpatial result)
    {
        try
        {
            switch (value)
            {
                // Spatial values are immutable: one already of the type is read as itself (DRY-010).
                case TSpatial spatial:
                    result = spatial;
                    return true;
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

    // Snowflake parses WKT, EWKT, (E)WKB hex and GeoJSON text into GEOGRAPHY/GEOMETRY (TYPE-002);
    // EWKT and EWKB keep the SRID.
    private static string CreateSnowflakeSpatial(SpatialValue value)
    {
        if (!string.IsNullOrEmpty(value.WellKnownText))
        {
            return value.WellKnownText.StartsWith("SRID=", StringComparison.OrdinalIgnoreCase)
                ? value.WellKnownText
                : string.Concat("SRID=", value.Srid.ToString(System.Globalization.CultureInfo.InvariantCulture), ";",
                    value.WellKnownText);
        }

        if (!value.WellKnownBinary.IsEmpty)
        {
            return Convert.ToHexString(AddSridToWkb(value.WellKnownBinary.Span, value.Srid));
        }

        return value.GeoJson ?? throw new NotSupportedException("The spatial value has no WKT, WKB or GeoJSON.");
    }

    private object? CreatePostgresSpatial(SpatialValue value)
    {
        if (!value.WellKnownBinary.IsEmpty)
        {
            return AddSridToWkb(value.WellKnownBinary.Span, value.Srid);
        }

        // The PostgreSQL family binds spatial values as bytea (EWKB); EWKT or GeoJSON text sent there
        // could never be read as a geometry (TYPE-002). WKT is encoded to WKB.
        if (!string.IsNullOrEmpty(value.WellKnownText))
        {
            return AddSridToWkb(WellKnownTextEncoder.Encode(value.WellKnownText), value.Srid);
        }

        throw new NotSupportedException(
            "PostgreSQL-family spatial values need WKB or WKT; a GeoJSON-only value cannot be written. " +
            "Create it with FromWellKnownText or FromWellKnownBinary.");
    }

    // SAP HANA's ST_GEOMETRY takes plain WKB as VARBINARY (it refuses WKT text) and stores the
    // column's SRID (TYPE-002). WKT is encoded to WKB.
    private static byte[] CreateWkb(SpatialValue value, string database)
    {
        if (!value.WellKnownBinary.IsEmpty)
        {
            return value.WellKnownBinary.ToArray();
        }

        if (!string.IsNullOrEmpty(value.WellKnownText))
        {
            return WellKnownTextEncoder.Encode(value.WellKnownText);
        }

        throw new NotSupportedException(
            database + " spatial values need WKB or WKT; a GeoJSON-only value cannot be written. " +
            "Create it with FromWellKnownText or FromWellKnownBinary.");
    }

    internal static byte[] AddSridToWkb(ReadOnlySpan<byte> wkb, int srid)
    {
        if (srid == 0 || wkb.Length < 5)
        {
            return wkb.ToArray();
        }

        var result = new byte[wkb.Length + 4];
        wkb[..5].CopyTo(result);
        var littleEndian = wkb[0] == 1;
        var type = littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(wkb[1..5])
            : BinaryPrimitives.ReadUInt32BigEndian(wkb[1..5]);
        if ((type & 0x20000000) != 0)
        {
            return wkb.ToArray();
        }
        type |= 0x20000000;
        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(1, 4), type);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(5, 4), srid);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(1, 4), type);
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(5, 4), srid);
        }

        wkb[5..].CopyTo(result.AsSpan(9));
        return result;
    }

    internal static string AddSridToWkt(string wkt, int srid)
    {
        return srid == 0 || wkt.StartsWith("SRID=", StringComparison.OrdinalIgnoreCase)
            ? wkt
            : $"SRID={srid};{wkt}";
    }

    private static string AddSridToGeoJson(string geoJson, int srid)
    {
        if (srid == 0)
        {
            return geoJson;
        }

        var node = JsonNode.Parse(geoJson) as JsonObject
            ?? throw new FormatException("Spatial GeoJSON must be a JSON object.");
        node["crs"] = new JsonObject
        {
            ["type"] = "name",
            ["properties"] = new JsonObject { ["name"] = $"EPSG:{srid}" }
        };
        return node.ToJsonString();
    }

    // MySQL/MariaDB store a geometry in their internal format: a 4-byte little-endian SRID
    // followed by standard WKB. Raw WKT bytes or bare WKB are rejected ("Cannot get geometry
    // object from data you send to the GEOMETRY field").
    private static object? CreateMySqlSpatial(SpatialValue value) =>
        pengdows.crud.types.coercion.MySqlSpatialFormat.Join(value);

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
        // MySQL/MariaDB return their internal format: a 4-byte little-endian SRID, then WKB.
        if (DatabaseTraits.For(provider).SpatialFormat == SpatialWireFormat.LittleEndianSridPrefixedWkb
            && bytes.Length > 4)
        {
            var srid = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes);
            return FromBinaryWithSrid(bytes.AsSpan(4), srid, bytes);
        }

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
