namespace pengdows.crud;

public abstract class AuditValueResolver : IAuditValueResolver
{
    public abstract IAuditValues Resolve();
}