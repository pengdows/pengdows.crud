#region

using System.Data;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.isolation;

public interface IIsolationResolver
{
    IsolationLevel Resolve(IsolationProfile profile);
    void Validate(IsolationLevel level);
    IReadOnlySet<IsolationLevel> GetSupportedLevels();
}