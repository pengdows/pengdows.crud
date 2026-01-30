// =============================================================================
// FILE: TenantConfiguration.cs
// PURPOSE: Configuration for a single tenant in multi-tenant setup.
//
// AI SUMMARY:
// - POCO class for tenant-specific database configuration.
// - Properties:
//   * Name: Tenant identifier (used as key in registry)
//   * DatabaseContextConfiguration: Full database settings for tenant
// - Designed for JSON/appsettings.json binding.
// - Each tenant gets isolated DatabaseContext with own connection string.
// - Used by TenantConnectionResolver for tenant lookup.
// =============================================================================

using pengdows.crud.configuration;

namespace pengdows.crud.tenant;

public class TenantConfiguration
{
    public string Name { get; set; } = string.Empty;
    public DatabaseContextConfiguration DatabaseContextConfiguration { get; set; } = new();
}