#region

#endregion

namespace pengdows.crud;

/// <summary>
/// Provides access to a secure string that is revealed only for the duration of an operation.
/// </summary>
public interface IEphemeralSecureString : IDisposable
{
    /// <summary>
    /// Reveals the protected string value.
    /// </summary>
    /// <returns>The underlying plain string.</returns>
    string Reveal();

    /// <summary>
    /// Executes an action with the revealed string and then re-secures it.
    /// </summary>
    /// <param name="use">Action to perform using the plain string.</param>
    void WithRevealed(Action<string> use);
}
