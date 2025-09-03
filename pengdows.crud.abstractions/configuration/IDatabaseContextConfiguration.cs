#region

using pengdows.crud.enums;

#endregion

namespace pengdows.crud.configuration;

/// <summary>
/// Defines configuration options for establishing and managing a database context.
/// </summary>
public interface IDatabaseContextConfiguration
{
    /// <summary>
    /// Gets or sets the connection string used to connect to the database.
    /// </summary>
    string ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the ADO.NET provider invariant name.
    /// </summary>
    string ProviderName { get; set; }

    /// <summary>
    /// Gets or sets the database engine mode to target.
    /// </summary>
    DbMode DbMode { get; set; }

    /// <summary>
    /// Gets or sets whether the context is in read-only, write-only, or read-write mode.
    /// </summary>
    ReadWriteMode ReadWriteMode { get; set; }

    /// <summary>
    /// When true, applies a default search_path of 'public' for PostgreSQL connections.
    /// </summary>
    bool SetDefaultSearchPath { get; set; }

    /// <summary>
    /// Override to force manual prepare on or off for all commands.
    /// When set, this overrides the dialect's PrepareStatements setting.
    /// </summary>
    bool? ForceManualPrepare { get; set; }

    /// <summary>
    /// When true, disables prepare for all commands regardless of dialect settings.
    /// Takes precedence over ForceManualPrepare.
    /// </summary>
    bool? DisablePrepare { get; set; }
}

