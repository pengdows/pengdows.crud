#region

using pengdow.crud;

#endregion

namespace testbed;

public class StringAuditContextProvider
    : IAuditValueResolver
{
    public string GetCurrentUserIdentifier()
    {
        return "testuser";
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