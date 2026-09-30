namespace pengdows.crud.exceptions;

/// <summary>
/// Marks an exception raised because a write was attempted through a read-only
/// context, connection, transaction, or database.
/// </summary>
public interface IReadOnlyViolation
{
}
