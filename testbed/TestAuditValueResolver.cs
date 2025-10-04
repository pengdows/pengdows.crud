using pengdows.crud;

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
            UserId = _userId,
            UtcNow = DateTime.UtcNow
        };
    }
}
