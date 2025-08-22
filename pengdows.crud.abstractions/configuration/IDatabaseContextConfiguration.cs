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
}

