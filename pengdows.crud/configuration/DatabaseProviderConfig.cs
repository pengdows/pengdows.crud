// =============================================================================
// FILE: DatabaseProviderConfig.cs
// PURPOSE: Configuration for loading a database provider dynamically.
//
// AI SUMMARY:
// - POCO for configuring DbProviderFactory loading from appsettings.
// - Properties:
//   * ProviderName: ADO.NET provider name (e.g., "Npgsql")
//   * FactoryType: Fully qualified factory type name
//   * AssemblyPath: File path to provider assembly (relative to app base)
//   * AssemblyName: Assembly name for Assembly.Load() fallback
// - Used by DbProviderLoader for dynamic provider registration.
// - Designed for JSON configuration binding.
// =============================================================================

namespace pengdows.crud.configuration;

public class DatabaseProviderConfig
{
    public string ProviderName { get; set; } = "";
    public string FactoryType { get; set; } = "";
    public string AssemblyPath { get; set; } = "";
    public string AssemblyName { get; set; } = "";

    /// <summary>
    /// Optional SHA-256 of the file at <see cref="AssemblyPath"/>, as 64 hexadecimal characters (case-insensitive).
    /// When set, a file whose bytes don't match is refused before it is loaded. Requires <see cref="AssemblyPath"/>.
    /// </summary>
    public string AssemblySha256 { get; set; } = "";
}