#region

using pengdows.crud;

#endregion

namespace testbed;

public class StringAuditContextProvider
    : IAuditValueResolver
{
    public IAuditValues Resolve()
    {
        var x = new AuditValues
        {
            UserId = GetCurrentUserIdentifier()
        };
        return x;
    }

    public string GetCurrentUserIdentifier()
    {
        return "testuser";
    }
}