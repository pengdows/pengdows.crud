using System.Data.Common;
using System.Reflection;
using System.Text.RegularExpressions;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

internal static partial class DbExceptionTranslationSupport
{
    public static DatabaseOperationException CreateFallback(
        SupportedDatabase database,
        Exception exception,
        DbOperationKind operationKind,
        bool? isTransient = null)
    {
        return new DatabaseOperationException(
            $"{operationKind} failed on {database}: {exception.Message}",
            database,
            exception,
            sqlState: TryGetSqlState(exception),
            errorCode: TryGetErrorCode(exception),
            constraintName: TryGetConstraintName(exception),
            isTransient: isTransient);
    }

    public static ConnectionException CreateConnection(
        SupportedDatabase database,
        Exception exception,
        DbOperationKind operationKind)
    {
        return new ConnectionException(
            $"{operationKind} encountered a connection failure on {database}: {exception.Message}",
            database,
            exception,
            sqlState: TryGetSqlState(exception),
            errorCode: TryGetErrorCode(exception));
    }

    public static CommandTimeoutException CreateTimeout(
        SupportedDatabase database,
        Exception exception,
        DbOperationKind operationKind)
    {
        return new CommandTimeoutException(
            $"{operationKind} timed out on {database}: {exception.Message}",
            database,
            exception,
            sqlState: TryGetSqlState(exception),
            errorCode: TryGetErrorCode(exception),
            constraintName: TryGetConstraintName(exception));
    }

    public static ReadOnlyViolationException CreateReadOnlyViolation(
        SupportedDatabase database,
        Exception exception,
        DbOperationKind operationKind)
    {
        return new ReadOnlyViolationException(
            $"{operationKind} attempted a write on a read-only {database} connection: {exception.Message}",
            database,
            exception,
            sqlState: TryGetSqlState(exception),
            errorCode: TryGetErrorCode(exception));
    }

    public static bool LooksLikeTimeout(Exception exception)
    {
        return exception is TimeoutException ||
               exception.GetType().Name.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
               (exception is DbException &&
                exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase));
    }

    public static int? TryGetErrorCode(Exception exception)
    {
        var type = exception.GetType();
        var property = type.GetProperty("Number", BindingFlags.Public | BindingFlags.Instance) ??
                       type.GetProperty("SqliteErrorCode", BindingFlags.Public | BindingFlags.Instance) ??
                       type.GetProperty("NativeError", BindingFlags.Public | BindingFlags.Instance);
        if (property != null)
        {
            var value = property.GetValue(exception);
            return value switch
            {
                int number => number,
                short number => number,
                long number when number <= int.MaxValue && number >= int.MinValue => (int)number,
                _ => null
            };
        }

        if (exception is DbException dbException && dbException.ErrorCode != 0)
        {
            return dbException.ErrorCode;
        }

        return TryGetErrorCodeFromErrorsCollection(exception);
    }

    /// <summary>
    /// Fallback for providers (e.g. AdoNetCore.AseClient's AseException) that expose a collection
    /// of provider-specific error records via a public "Errors" property instead of a single
    /// top-level error-code property, and whose exception type does not derive from
    /// <see cref="DbException"/> at all (so the checks above never apply).
    /// </summary>
    private static int? TryGetErrorCodeFromErrorsCollection(Exception exception)
    {
        var errorsProperty = exception.GetType().GetProperty("Errors", BindingFlags.Public | BindingFlags.Instance);
        if (errorsProperty?.GetValue(exception) is not System.Collections.IEnumerable errors)
        {
            return null;
        }

        foreach (var error in errors)
        {
            var numberProperty = error.GetType().GetProperty("MessageNumber", BindingFlags.Public | BindingFlags.Instance) ??
                                  error.GetType().GetProperty("Number", BindingFlags.Public | BindingFlags.Instance);
            if (numberProperty?.GetValue(error) is int number)
            {
                return number;
            }
        }

        return null;
    }

    public static string? TryGetSqlState(Exception exception)
    {
        if (exception is DbException dbException && !string.IsNullOrWhiteSpace(dbException.SqlState))
        {
            return dbException.SqlState;
        }

        var property = exception.GetType().GetProperty("SqlState", BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetValue(exception) is string sqlState && !string.IsNullOrWhiteSpace(sqlState))
        {
            return sqlState;
        }

        return TryGetSqlStateFromErrorsCollection(exception);
    }

    private static string? TryGetSqlStateFromErrorsCollection(Exception exception)
    {
        var errorsProperty = exception.GetType().GetProperty("Errors", BindingFlags.Public | BindingFlags.Instance);
        if (errorsProperty?.GetValue(exception) is not System.Collections.IEnumerable errors)
        {
            return null;
        }

        foreach (var error in errors)
        {
            var sqlStateProperty = error.GetType().GetProperty("SqlState", BindingFlags.Public | BindingFlags.Instance);
            if (sqlStateProperty?.GetValue(error) is string sqlState && !string.IsNullOrWhiteSpace(sqlState))
            {
                return sqlState;
            }
        }

        return null;
    }

    public static string? TryGetConstraintName(Exception exception)
    {
        var property = exception.GetType().GetProperty("ConstraintName", BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetValue(exception) is string constraintName && !string.IsNullOrWhiteSpace(constraintName))
        {
            return constraintName;
        }

        var message = exception.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var match = ConstraintNameRegex().Match(message);
        return match.Success ? match.Groups["name"].Value : null;
    }

    [GeneratedRegex("constraint\\s+'?(?<name>[^'\\s\\)]+)'?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConstraintNameRegex();
}
