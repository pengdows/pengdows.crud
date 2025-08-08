#region

using System.Data;
using pengdow.crud.enums;

#endregion

namespace pengdow.crud.isolation;

public interface IIsolationResolver
{
    IsolationLevel Resolve(IsolationProfile profile);
    void Validate(IsolationLevel level);
    IReadOnlySet<IsolationLevel> GetSupportedLevels();
}