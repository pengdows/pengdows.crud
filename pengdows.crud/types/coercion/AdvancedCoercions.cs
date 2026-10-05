// =============================================================================
// FILE: AdvancedCoercions.cs
// PURPOSE: Advanced type coercions for database-specific exotic types.
//
// AI SUMMARY:
// - RegisterAll(): HierarchyId, plus the database-specific read coercions each dialect's traits declare.
// - Types with an AdvancedTypeRegistry converter are read by the converter alone (DRY-010).
// - MySqlSpatialFormat: the MySQL family's internal spatial format (4-byte LE SRID + WKB), used by the
//   spatial converters and type mappings.
// - PackedFloat32VectorCoercion: SingleStore VECTOR(n) bytes into float[].
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.types.coercion;

/// <summary>
/// Advanced type coercions for database-specific types.
/// Handles spatial, network, temporal, and large object types.
/// </summary>
internal static class AdvancedCoercions
{
    // Reading an NpgsqlInterval/NpgsqlRange back does not depend on the database: the converters
    // use their database argument only when writing.
    internal const SupportedDatabase AnyDatabase = SupportedDatabase.Unknown;

    public static void RegisterAll(CoercionRegistry registry)
    {
        // A type with an AdvancedTypeRegistry converter (spatial, network, intervals, ranges, row
        // versions, LOB streams, JsonDocument) is read by its converter alone (DRY-010); it has no
        // coercion here. Database-specific coercions (SingleStore's packed float32 vectors, SAP HANA's
        // arrays) are declared by the dialects (REV-039).
        foreach (var traits in DatabaseTraits.All)
        {
            traits.RegisterCoercions?.Invoke(registry);
        }

        // Hierarchies (TYPE-016)
        registry.Register(new HierarchyIdCoercion());
    }
}

/// <summary>
/// Coercion for <see cref="HierarchyId"/> (TYPE-016): reads SQL Server's stored encoding (what
/// SqlClient's GetBytes returns without Microsoft.SqlServer.Types), that assembly's SqlHierarchyId,
/// or the text form; writes the text form, which SQL Server converts to hierarchyid implicitly.
/// </summary>
internal class HierarchyIdCoercion : DbCoercion<HierarchyId>
{
    public override bool TryRead(in DbValue src, out HierarchyId value)
    {
        value = default;
        if (src.IsNull)
        {
            return false;
        }

        try
        {
            switch (src.RawValue)
            {
                case HierarchyId hierarchyId:
                    value = hierarchyId;
                    return true;
                case string text:
                    return HierarchyId.TryParse(text, out value);
                case byte[] bytes:
                    value = HierarchyId.FromSqlServerBytes(bytes);
                    return true;
                case { } other when other.GetType().Name == "SqlHierarchyId":
                    return HierarchyId.TryParse(other.ToString(), out value);
                default:
                    return false;
            }
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// MySQL-family GEOMETRY: the server's internal format, a 4-byte little-endian SRID followed by
/// standard WKB, on both read and write (TYPE-018).
/// </summary>
internal static class MySqlSpatialFormat
{
    public static bool TrySplit(object? raw, out int srid, out byte[] wkb)
    {
        if (raw is byte[] bytes && bytes.Length > 4)
        {
            srid = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes);
            wkb = bytes.AsSpan(4).ToArray();
            return true;
        }

        srid = 0;
        wkb = Array.Empty<byte>();
        return false;
    }

    public static byte[] Join(SpatialValue value)
    {
        byte[] wkb;
        if (!value.WellKnownBinary.IsEmpty)
        {
            GeometryConverter.ExtractSridFromEwkb(value.WellKnownBinary.Span, out _, out wkb);
        }
        else if (!string.IsNullOrEmpty(value.WellKnownText))
        {
            wkb = WellKnownTextEncoder.Encode(value.WellKnownText);
        }
        else
        {
            throw new NotSupportedException(
                "MySQL spatial values need WKB or WKT; a GeoJSON-only value cannot be written. " +
                "Create it with FromWellKnownText or FromWellKnownBinary.");
        }

        var internalFormat = new byte[4 + wkb.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(internalFormat, value.Srid);
        wkb.CopyTo(internalFormat, 4);
        return internalFormat;
    }
}

/// <summary>
/// SingleStore VECTOR(n) (F32): read as packed little-endian float32 bytes (confirmed live); written
/// as JSON array text (SqlDialect.BindsVectorsAsText).
/// </summary>
internal sealed class PackedFloat32VectorCoercion : DbCoercion<float[]>
{
    public override bool TryRead(in DbValue src, out float[] value)
    {
        if (src.RawValue is byte[] bytes && bytes.Length % 4 == 0)
        {
            value = new float[bytes.Length / 4];
            for (var i = 0; i < value.Length; i++)
            {
                value[i] = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(4 * i));
            }

            return true;
        }

        value = null!;
        return false;
    }
}
