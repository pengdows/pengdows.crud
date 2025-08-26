namespace pengdows.crud.dialects;

/// <summary>
/// Provides access to the active <see cref="ISqlDialect"/> instance.
/// </summary>
internal interface ISqlDialectProvider
{
    /// <summary>
    /// Gets the SQL dialect in use.
    /// </summary>
    ISqlDialect Dialect { get; }
}
