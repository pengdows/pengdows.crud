#region

using pengdows.crud;
using System.Text.Json;

#endregion

namespace testbed;

public class StringAuditContextProvider
    : IAuditValueResolver
{
    public string GetCurrentUserIdentifier()
    {
        return JsonSerializer.Serialize("testuser");
    }

    public IAuditValues Resolve()
    {
        var x = new AuditValues
        {
            UserId = GetCurrentUserIdentifier()
        };
        return x;
    }
}
