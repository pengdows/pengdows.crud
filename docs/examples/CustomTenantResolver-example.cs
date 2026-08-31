// =============================================================================
// FILE: CustomTenantResolver-example.cs
// PURPOSE: Example/reference implementation of a fully custom
//          ITenantConnectionResolver that loads tenant -> connection-string
//          mappings from a "control-plane" database, for applications that
//          can't express their tenant list as static appsettings.json
//          configuration (see docs/connection/multitenancy.md's
//          "Custom resolver" section for the narrative version of this file).
//
// AI SUMMARY:
// - SqlControlPlaneTenantResolver implements ITenantConnectionResolver by
//   querying a small "tenants" table (tenant_name, provider_name,
//   connection_string, db_mode) via plain ADO.NET against a control-plane
//   database — deliberately NOT via pengdows.crud itself, to avoid a
//   chicken-and-egg dependency on the very tenant registry being built.
// - LoadAsync() populates/refreshes an internal ConcurrentDictionary-backed
//   TenantConnectionResolver; GetDatabaseContextConfiguration delegates to it.
// - RefreshTenantAsync(tenantId) demonstrates the two-step
//   Register-then-Invalidate rotation pattern documented in multitenancy.md's
//   "Invalidate/InvalidateAll" section — a genuinely supported live-rotation
//   workflow. Any caller holding the affected tenant's context across an
//   await during the rotation should use AcquireLease/AcquireLeaseAsync
//   instead of a bare GetContext/GetContextAsync reference (see multitenancy.md's
//   "Protecting against concurrent rotation" section) so it isn't disposed
//   out from under it mid-use.
// - Program.cs wiring at the bottom shows the full "Option A" DI setup:
//   keyed DbProviderFactory registrations, IDatabaseContextFactory, and
//   ITenantContextRegistry — everything AddMultiTenancy would otherwise do
//   for you, done by hand because the tenant list isn't static configuration.
// =============================================================================

using System.Collections.Concurrent;
using System.Data.Common;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.tenant;

namespace pengdows.crud.examples;

/// <summary>
/// Resolves tenant database configuration from a control-plane database's "tenants" table,
/// instead of static appsettings.json configuration. Use this shape when the tenant list is
/// large, dynamic, or provisioned by a separate onboarding system — anywhere
/// <c>AddMultiTenancy</c>'s static <c>MultiTenant:Tenants</c> configuration section doesn't fit.
/// </summary>
/// <remarks>
/// Register this as a singleton alongside <c>IDatabaseContextFactory</c> and
/// <c>ITenantContextRegistry</c> — see the DI wiring example at the bottom of this file.
/// <c>ITenantConnectionResolver</c> has no built-in refresh/polling of its own; this class owns
/// that decision (call <see cref="LoadAsync"/> at startup, and optionally on a timer or webhook).
/// </remarks>
public sealed class SqlControlPlaneTenantResolver : ITenantConnectionResolver
{
    private readonly DbProviderFactory _controlPlaneFactory;
    private readonly string _controlPlaneConnectionString;
    private readonly TenantConnectionResolver _inner = new();

    public SqlControlPlaneTenantResolver(DbProviderFactory controlPlaneFactory, string controlPlaneConnectionString)
    {
        _controlPlaneFactory = controlPlaneFactory;
        _controlPlaneConnectionString = controlPlaneConnectionString;
    }

    public IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant) =>
        _inner.GetDatabaseContextConfiguration(tenant);

    /// <summary>
    /// Loads (or reloads) every tenant row from the control-plane database. Call once at startup
    /// before the application starts serving requests; call again on whatever cadence/trigger your
    /// deployment uses to notice new or changed tenants (a timer, an admin-triggered webhook, etc.)
    /// — this class does not schedule that for you.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _controlPlaneFactory.CreateConnection()
                                      ?? throw new InvalidOperationException("Control-plane provider factory returned no connection.");
        connection.ConnectionString = _controlPlaneConnectionString;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT tenant_name, provider_name, connection_string, db_mode FROM tenants";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var tenantName = reader.GetString(0);
            var config = new DatabaseContextConfiguration
            {
                ProviderName = reader.GetString(1),
                ConnectionString = reader.GetString(2),
                DbMode = Enum.Parse<DbMode>(reader.GetString(3))
            };
            _inner.Register(tenantName, config);
        }
    }

    /// <summary>
    /// Re-reads one tenant's row and, if found, re-registers its configuration and invalidates the
    /// cached context so the next <c>GetContext</c> call picks up the change. This is the same
    /// two-step Register-then-Invalidate sequence documented in multitenancy.md — a genuinely
    /// supported live-rotation workflow when wired into an admin-triggered "update tenant"
    /// endpoint. Any request already mid-flight against the pre-rotation context is safe only if
    /// it acquired that context via <c>AcquireLease</c>/<c>AcquireLeaseAsync</c> rather than a bare
    /// <c>GetContext</c>/<c>GetContextAsync</c> reference — see multitenancy.md's "Protecting
    /// against concurrent rotation" section.
    /// </summary>
    public async Task RefreshTenantAsync(string tenantId, ITenantContextRegistry registry,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _controlPlaneFactory.CreateConnection()
                                      ?? throw new InvalidOperationException("Control-plane provider factory returned no connection.");
        connection.ConnectionString = _controlPlaneConnectionString;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT provider_name, connection_string, db_mode FROM tenants WHERE tenant_name = @tenant";
        var param = command.CreateParameter();
        param.ParameterName = "@tenant";
        param.Value = tenantId;
        command.Parameters.Add(param);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return; // Tenant no longer exists in the control plane — leave the cached context alone.
        }

        var config = new DatabaseContextConfiguration
        {
            ProviderName = reader.GetString(0),
            ConnectionString = reader.GetString(1),
            DbMode = Enum.Parse<DbMode>(reader.GetString(2))
        };

        _inner.Register(tenantId, config);
        registry.Invalidate(tenantId);
    }
}

// -----------------------------------------------------------------------------
// Program.cs / Startup wiring ("Option A" — fully custom resolver, no
// AddMultiTenancy call, since the tenant list isn't static configuration):
// -----------------------------------------------------------------------------
//
// // 1. Register a keyed DbProviderFactory for every ADO.NET provider your tenants use.
// //    The key is whatever ProviderName your control-plane rows store — it does not need to
// //    match the provider's ADO.NET invariant name (see dynamic-provider-loading.md if you want
// //    this step driven by configuration too, via AddDbProviderLoading).
// builder.Services.AddKeyedSingleton<DbProviderFactory>("postgres", Npgsql.NpgsqlFactory.Instance);
// builder.Services.AddKeyedSingleton<DbProviderFactory>("sqlserver", Microsoft.Data.SqlClient.SqlClientFactory.Instance);
//
// // 2. Register your custom resolver as a singleton, and load it once at startup.
// builder.Services.AddSingleton(sp =>
// {
//     var resolver = new SqlControlPlaneTenantResolver(
//         Npgsql.NpgsqlFactory.Instance,               // the control-plane DB's own factory
//         builder.Configuration.GetConnectionString("ControlPlane")!);
//     resolver.LoadAsync().GetAwaiter().GetResult();   // or await it from an IHostedService instead
//     return resolver;
// });
// builder.Services.AddSingleton<ITenantConnectionResolver>(sp => sp.GetRequiredService<SqlControlPlaneTenantResolver>());
//
// // 3. IDatabaseContextFactory and ITenantContextRegistry — AddMultiTenancy registers both of
// //    these for you; a fully custom setup registers them directly instead.
// builder.Services.AddSingleton<IDatabaseContextFactory, DefaultDatabaseContextFactory>();
// builder.Services.AddSingleton<ITenantContextRegistry, TenantContextRegistry>();
//
// // 4. Use it exactly like the AddMultiTenancy path — inject ITenantContextRegistry at request
// //    time and pass the resolved IDatabaseContext into your (singleton) TableGateway calls.
