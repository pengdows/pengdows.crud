namespace pengdows.crud.exceptions;

/// <summary>
/// Marks an exception raised by execution-governance admission control — a connection pool,
/// mode-lock, or forbidden-pool rejection — rather than a database-level failure. Implemented by
/// <c>PoolSaturatedException</c>, <c>ModeContentionException</c>, and
/// <c>PoolForbiddenException</c>, which deliberately extend different base types
/// (<see cref="System.TimeoutException"/>/<see cref="System.InvalidOperationException"/>) and are
/// not <c>DatabaseException</c> subclasses, so this marker lets a caller (or
/// <c>RetryContext</c>) detect all three as one group without unifying their inheritance.
/// </summary>
public interface IExecutionGovernanceRejection
{
}
