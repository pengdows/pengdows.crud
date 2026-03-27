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

        return null;
    }

    public static string? TryGetSqlState(Exception exception)
    {
        if (exception is DbException dbException && !string.IsNullOrWhiteSpace(dbException.SqlState))
        {
            return dbException.SqlState;
        }

        var property = exception.GetType().GetProperty("SqlState", BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(exception) as string;
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
