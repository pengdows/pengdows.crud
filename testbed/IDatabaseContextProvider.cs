#region

using pengdows.crud;

#endregion

namespace testbed;

public interface IDatabaseContextProvider
{
    /// <summary>
    /// Gets a database context for the specified key.
    /// </summary>
    /// <param name="key">Provider key or name.</param>
    /// <returns>The resolved database context.</returns>
    IDatabaseContext Get(string key);
}
