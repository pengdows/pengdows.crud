namespace pengdows.crud;

public class StubAuditValueResolver : AuditValueResolver
{
    private readonly object _userId;

    public StubAuditValueResolver(object userId)
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