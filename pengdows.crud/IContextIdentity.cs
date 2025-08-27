#region

#endregion

namespace pengdows.crud;

/// <summary>
/// Represents an immutable identifier for the current execution context.
/// </summary>
internal interface IContextIdentity
{
    /// <summary>
    /// Identifier for the root request or operation.
    /// </summary>
    Guid RootId { get; }
}
