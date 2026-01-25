namespace pengdows.crud;

public sealed class AuditValues : IAuditValues
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;

    public required object UserId { get; init; }

    public T As<T>()
    {
        return (T)UserId;
    }
}