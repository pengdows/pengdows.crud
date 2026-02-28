using System.Data.Common;

namespace pengdows.crud.@internal;

/// <summary>
/// Internal interface for unwrapping connection wrappers to get the underlying physical connection.
/// </summary>
internal interface IInternalConnectionWrapper
{
    /// <summary>
    /// Gets the underlying physical connection.
    /// </summary>
    DbConnection UnderlyingConnection { get; }
}
