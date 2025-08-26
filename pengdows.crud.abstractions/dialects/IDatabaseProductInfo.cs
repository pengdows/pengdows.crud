#region

using System;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.dialects;

/// <summary>
/// Describes product information discovered from a database connection.
/// </summary>
public interface IDatabaseProductInfo
{
    /// <summary>
    /// Friendly name of the database product.
    /// </summary>
    string ProductName { get; set; }

    /// <summary>
    /// Raw version string reported by the database.
    /// </summary>
    string ProductVersion { get; set; }

    /// <summary>
    /// Parsed representation of <see cref="ProductVersion"/>, when available.
    /// </summary>
    Version? ParsedVersion { get; set; }

    /// <summary>
    /// Detected database type.
    /// </summary>
    SupportedDatabase DatabaseType { get; set; }

    /// <summary>
    /// Highest SQL standard level the database claims to comply with.
    /// </summary>
    SqlStandardLevel StandardCompliance { get; set; }
}
