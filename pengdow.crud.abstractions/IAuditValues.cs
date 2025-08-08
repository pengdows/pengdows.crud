namespace pengdow.crud;

public interface IAuditValues
{
    object UserId { get; init; }
    DateTime UtcNow { get; }

    T As<T>()
    {
        return (T)UserId;
    }
}