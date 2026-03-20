using pengdows.crud.enums;

namespace pengdows.crud.exceptions;

/// <summary>
/// Thrown in strict mode when a column value cannot be coerced to the target property type.
/// </summary>
/// <remarks>
/// <see cref="DatabaseException.Database"/> is always <see cref="pengdows.crud.enums.SupportedDatabase.Unknown"/>
/// because <c>DataReaderMapper</c> is database-agnostic. The
/// <see cref="Exception.InnerException"/> contains the original coercion or cast error.
/// </remarks>
public sealed class DataMappingException : DatabaseException
{
    public DataMappingException(
        string message,
        SupportedDatabase database,
        Exception? innerException = null,
        string? sqlState = null,
        int? errorCode = null,
        string? constraintName = null,
        bool? isTransient = null)
        : base(message, database, innerException, sqlState, errorCode, constraintName, isTransient)
    {
    }
}

/// <summary>
/// Thrown when entity metadata validation fails during SQL generation setup.
/// </summary>
/// <remarks>
/// Signals a programmer error — e.g. missing <c>[Table]</c> attribute, empty column name,
/// duplicate column, invalid enum DbType, or missing key definition. These exceptions fire
/// at registration/construction time, never at query execution time.
/// <see cref="DatabaseException.Database"/> is always <see cref="pengdows.crud.enums.SupportedDatabase.Unknown"/>
/// because the failure occurs before any database context is established.
/// </remarks>
public sealed class SqlGenerationException : DatabaseException
{
    public SqlGenerationException(
        string message,
        SupportedDatabase database,
        Exception? innerException = null,
        string? sqlState = null,
        int? errorCode = null,
        string? constraintName = null,
        bool? isTransient = null)
        : base(message, database, innerException, sqlState, errorCode, constraintName, isTransient)
    {
    }
}
