// =============================================================================
// FILE: MultiTenantOptions.cs
// PURPOSE: Options class for multi-tenant configuration from appsettings.
//
// AI SUMMARY:
// - POCO for binding "MultiTenant" section from configuration.
// - Properties:
//   * ApplicationName: Base app name, composed with tenant name
//   * Tenants: List of TenantConfiguration objects
// - Application name composition: "{ApplicationName}:{TenantName}".
// - Used by TenantServiceCollectionExtensions.AddMultiTenancy().
// - Designed for IOptions<MultiTenantOptions> pattern.
// - Example appsettings.json: { "MultiTenant": { "ApplicationName": "MyApp", "Tenants": [...] } }
// =============================================================================

namespace pengdows.crud.tenant;

public class MultiTenantOptions
{
    public string ApplicationName { get; set; } = string.Empty;
    public List<TenantConfiguration> Tenants { get; init; } = new();
}
