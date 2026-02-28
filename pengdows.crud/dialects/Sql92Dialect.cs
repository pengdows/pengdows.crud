// =============================================================================
// FILE: Sql92Dialect.cs
// PURPOSE: Fallback SQL-92 compliant dialect for unknown/unsupported databases.
//
// AI SUMMARY:
// - Used when the database type cannot be detected or isn't supported.
// - Provides basic SQL-92 standard compliance with positional parameters (?).
// - Named parameters are NOT assumed — positional is the safest fallback.
// - Minimal feature support: no MERGE, no UPSERT, no DROP TABLE IF EXISTS,
//   no INSERT RETURNING, no identity columns, no savepoints.
// - Logs a Warning on construction to signal fallback dialect is active.
// - Use GetCompatibilityWarning() to surface the issue to callers.
// - IsFallbackDialect returns true for this dialect.
// =============================================================================

using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.dialects;

/// <summary>
/// Fallback SQL-92 dialect for unsupported or unknown databases.
/// </summary>
/// <remarks>
/// <para>
/// This dialect is used when the database type cannot be detected or when
/// using an unsupported database provider. It assumes the least possible
/// capability to avoid generating SQL that silently misbehaves.
/// </para>
/// <para>
/// <strong>Positional parameters only:</strong> Named parameter support is
/// not assumed. All parameters use <c>?</c> placeholders.
/// SQL-92 does not standardize parameter markers; <c>?</c> is the safest
/// positional fallback (ODBC convention).
/// </para>
/// <para>
/// <strong>Disabled features:</strong> MERGE, INSERT RETURNING,
/// DROP TABLE IF EXISTS, identity columns, savepoints.
/// Any dialect that supports these must be detected and used explicitly.
/// </para>
/// <para>
/// A <see cref="LogLevel.Warning"/> is emitted on construction.
/// Use <see cref="ISqlDialect.GetCompatibilityWarning"/> to surface the
/// message to callers.
/// </para>
/// </remarks>
internal class Sql92Dialect : SqlDialect
{
    /// <summary>
    /// Initializes a new SQL-92 fallback dialect and logs a warning.
    /// </summary>
    internal Sql92Dialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
        logger.LogWarning(
            "SQL-92 fallback dialect is active. The database type could not be detected. " +
            "Advanced features (MERGE, RETURNING, DROP TABLE IF EXISTS, identity columns) " +
            "are disabled. Positional parameters (?) will be used. " +
            "Verify your connection string and provider configuration.");
    }

    /// <inheritdoc />
    public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;

    /// <summary>
    /// Uses positional <c>?</c> placeholder. Named parameters are not assumed
    /// for an unknown database — positional is the safest ODBC-compatible fallback.
    /// </summary>
    public override string ParameterMarker => "?";

    /// <summary>
    /// Named parameters are not supported by this fallback dialect.
    /// All parameters use positional <c>?</c> placeholders.
    /// </summary>
    public override bool SupportsNamedParameters => false;

    /// <summary>
    /// DROP TABLE IF EXISTS is not guaranteed by SQL-92 and is disabled for
    /// the fallback dialect to prevent silent SQL generation errors.
    /// </summary>
    public override bool SupportsDropTableIfExists => false;
}