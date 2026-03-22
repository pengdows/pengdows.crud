using pengdows.crud.enums;

namespace pengdows.crud.exceptions;

/// <summary>
/// Base class for all pengdows.crud database exceptions.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="System.OperationCanceledException"/> is never wrapped in a
/// <c>DatabaseException</c>. Cancellation propagates as-is so callers can use a
/// standard <c>catch (OperationCanceledException)</c> without also catching the
/// full exception hierarchy.
/// </para>
/// <para>
/// Concrete subtypes: <see cref="DatabaseOperationException"/> (runtime failures),
/// <see cref="SqlGenerationException"/> (entity metadata errors),
/// <see cref="DataMappingException"/> (column-to-property mapping failures in strict mode).
/// </para>
/// </remarks>
public abstract class DatabaseException : Exception
{
    protected DatabaseException(
        string message,
        SupportedDatabase database,
        Exception? innerException = null,
        string? sqlState = null,
        int? errorCode = null,
        string? constraintName = null,
        bool? isTransient = null)
        : base(message, innerException)
    {
        Database = database;
        SqlState = sqlState;
        ErrorCode = errorCode;
        ConstraintName = constraintName;
        IsTransient = isTransient;
    }

    public SupportedDatabase Database { get; }

    public string? SqlState { get; }

    public int? ErrorCode { get; }

    public string? ConstraintName { get; }

    public bool? IsTransient { get; }
}
