using pengdows.crud.enums;

namespace pengdows.crud;

/// <summary>
/// Provider-agnostic analysis of a database exception.
/// </summary>
/// <param name="Category">High-level error category for metrics and control flow.</param>
/// <param name="ConstraintKind">Specific constraint kind when <see cref="Category"/> is <see cref="DbErrorCategory.ConstraintViolation"/>.</param>
/// <param name="IsTransient">True when the operation may succeed if retried later.</param>
/// <param name="IsRetryable">True when the caller should generally retry the operation.</param>
/// <param name="ProviderErrorCode">Provider-specific numeric error code when available.</param>
/// <param name="SqlState">SQLSTATE code when available.</param>
public readonly record struct DbExceptionInfo(
    DbErrorCategory Category,
    DbConstraintKind ConstraintKind,
    bool IsTransient,
    bool IsRetryable,
    int? ProviderErrorCode,
    string? SqlState);
