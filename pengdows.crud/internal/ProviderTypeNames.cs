// =============================================================================
// FILE: ProviderTypeNames.cs
// PURPOSE: The provider types the read path recognises by name, in one place.
//
// AI SUMMARY:
// - The libraries that define these types (Npgsql, SqlClient) are not referenced by this assembly, so a provider value
//   is recognised by its full name. Every such name is kept here, so a reviewer finds them in one file.
// - Each predicate matches one type exactly (or one generic definition); none matches by prefix except the Npgsql
//   range family, which covers its closed and generic forms.
// =============================================================================

using System;

namespace pengdows.crud.@internal;

internal static class ProviderTypeNames
{
    public static bool IsNpgsqlInterval(Type type) => type.FullName == "NpgsqlTypes.NpgsqlInterval";

    public static bool IsNpgsqlInet(Type type) => type.FullName == "NpgsqlTypes.NpgsqlInet";

    public static bool IsNpgsqlCidr(Type type) => type.FullName == "NpgsqlTypes.NpgsqlCidr";

    public static bool IsNpgsqlRange(Type type) => type.FullName?.StartsWith("NpgsqlTypes.NpgsqlRange", StringComparison.Ordinal) == true;

    public static bool IsNpgsqlRangeDefinition(Type type) => type.FullName == "NpgsqlTypes.NpgsqlRange`1";

    public static bool IsSqlHierarchyId(Type type) => type.Name == "SqlHierarchyId";

    /// <summary>A DuckDB.NET reader, by its type name's prefix.</summary>
    public static bool IsDuckDbReaderTypeName(string? readerTypeName) =>
        readerTypeName != null && readerTypeName.StartsWith("DuckDB", StringComparison.Ordinal);

    /// <summary>Firebird's zoned timestamp value (FbZonedDateTime).</summary>
    public static bool IsFirebirdZonedDateTime(Type type) => type.FullName == "FirebirdSql.Data.Types.FbZonedDateTime";

    /// <summary>Firebird's zoned time value (FbZonedTime).</summary>
    public static bool IsFirebirdZonedTime(Type type) => type.FullName == "FirebirdSql.Data.Types.FbZonedTime";

    /// <summary>A SQL Server spatial value (SqlGeometry or SqlGeography, from SqlClient).</summary>
    public static bool IsSqlSpatial(Type type) =>
        (type.FullName ?? string.Empty).Contains("SqlGeometry", StringComparison.OrdinalIgnoreCase) ||
        (type.FullName ?? string.Empty).Contains("SqlGeography", StringComparison.OrdinalIgnoreCase);
}
