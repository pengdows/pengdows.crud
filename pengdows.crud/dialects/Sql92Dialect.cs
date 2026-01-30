// =============================================================================
// FILE: Sql92Dialect.cs
// PURPOSE: Fallback SQL-92 compliant dialect for unknown/unsupported databases.
//
// AI SUMMARY:
// - Used when the database type cannot be detected or isn't supported.
// - Provides basic SQL-92 standard compliance.
// - Uses @ as parameter marker (most common default).
// - Minimal feature support (no MERGE, no UPSERT, basic quoting).
// - Will generate compatibility warnings via GetCompatibilityWarning().
// - IsFallbackDialect returns true for this dialect.
// =============================================================================

using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;

namespace pengdows.crud.dialects;

/// <summary>
/// Fallback SQL-92 dialect for unsupported or unknown databases.
/// </summary>
/// <remarks>
/// <para>
/// This dialect is used when the database type cannot be detected or when
/// using an unsupported database provider. It provides minimal SQL-92
/// compliant behavior.
/// </para>
/// <para>
/// <strong>Limitations:</strong> No MERGE support, no UPSERT support,
/// basic identifier quoting. Use <see cref="ISqlDialect.GetCompatibilityWarning"/>
/// to check for compatibility issues.
/// </para>
/// </remarks>
internal class Sql92Dialect : SqlDialect
{
    /// <summary>
    /// Initializes a new SQL-92 fallback dialect.
    /// </summary>
    internal Sql92Dialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    /// <inheritdoc />
    public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;

    /// <inheritdoc />
    public override string ParameterMarker => "@";
}