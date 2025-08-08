#region

using pengdow.crud;

#endregion

namespace testbed;

public interface IDatabaseContextProvider
{
    IDatabaseContext Get(string key);
}