#region

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
    /// SQL feature capability tier indicating which features are available.
    /// <para>
    /// This is NOT a measure of ISO SQL standard conformance, but rather a heuristic
    /// for estimating feature availability based on database version.
    /// </para>
    /// </summary>
    SqlStandardLevel StandardCompliance { get; set; }
}