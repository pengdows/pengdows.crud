#region

using pengdows.crud;

#endregion

namespace testbed;

public interface IDatabaseContextProvider
{
    IDatabaseContext Get(string key);
}