// =============================================================================
// FILE: DatabaseProductInfo.cs
// PURPOSE: Holds detected database product metadata (name, version, type).
//
// AI SUMMARY:
// - Populated by SqlDialect.DetectDatabaseInfoAsync from connection metadata.
// - Contains: ProductName (e.g., "PostgreSQL"), ProductVersion (e.g., "14.2"),
//   ParsedVersion (as System.Version), DatabaseType enum, StandardCompliance.
// - Used by DataSourceInformation to expose database capabilities.
// - StandardCompliance indicates SQL standard level (Sql92, Sql99, Sql2003, etc.).
// =============================================================================

using pengdows.crud.enums;

namespace pengdows.crud.dialects;

/// <summary>
/// Contains database product information detected from the connection.
/// </summary>
/// <remarks>
/// This class is populated during dialect initialization with metadata
/// queried from the database connection or inferred from the provider.
/// </remarks>
/// <seealso cref="IDatabaseProductInfo"/>
/// <seealso cref="ISqlDialect.ProductInfo"/>
public class DatabaseProductInfo : IDatabaseProductInfo
{
    /// <summary>
    /// Gets or sets the database product name (e.g., "Microsoft SQL Server", "PostgreSQL").
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the database version string (e.g., "14.2", "15.0.2000.5").
    /// </summary>
    public string ProductVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the parsed version for programmatic comparison.
    /// </summary>
    public Version? ParsedVersion { get; set; }

    /// <summary>
    /// Gets or sets the database type enum for dialect selection.
    /// </summary>
    public SupportedDatabase DatabaseType { get; set; }

    /// <summary>
    /// Gets or sets the SQL standard compliance level.
    /// </summary>
    public SqlStandardLevel StandardCompliance { get; set; } = SqlStandardLevel.Sql92;
}