using pengdows.crud;
using System.Text.Json;

namespace testbed;

public class TestAuditValueResolver : AuditValueResolver
{
    private readonly object _userId;

    public TestAuditValueResolver(object userId)
    {
        _userId = userId;
    }

    public override IAuditValues Resolve()
    {
        return new AuditValues
        {
            UserId = _userId is string s ? JsonSerializer.Serialize(s) : _userId,
            UtcNow = DateTime.UtcNow
        };
    }
}
