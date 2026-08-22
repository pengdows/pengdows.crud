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
        // Some providers (e.g. Npgsql on a client-side CommandTimeout) wrap the real
        // TimeoutException inside an outer DbException whose own type name and message contain
        // no "timeout" wording at all (e.g. NpgsqlException("Exception while reading from
        // stream") wrapping TimeoutException("Timeout during reading attempt")) — walk the
        // InnerException chain rather than inspecting only the outermost exception.
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is TimeoutException ||
                current.GetType().Name.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
                (current is DbException &&
                 current.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
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

        // Case-insensitive, ambiguity-safe lookup: IBM's DB2Exception declares its OWN
        // "SQLState" (all-caps SQL) property alongside the inherited DbException.SqlState —
        // a plain GetProperty(name, IgnoreCase) throws AmbiguousMatchException in that shape.
        // Confirmed against a live ibmcom/db2 container during Phase 2 testbed validation.
        foreach (var property in exception.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType == typeof(string) &&
                string.Equals(property.Name, "SqlState", StringComparison.OrdinalIgnoreCase) &&
                property.GetValue(exception) is string candidate &&
                !string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        // Last-resort fallback: some providers embed the SQLSTATE directly in the exception
        // message rather than exposing it as a queryable property, in one of two formats:
        // trailing "... SQLSTATE=23505" (e.g. client-side CLI driver errors), or leading
        // "ERROR [23505] ..." (e.g. IBM.Data.Db2's server-side error messages).
        var match = SqlStateFromMessageRegex().Match(exception.Message ?? string.Empty);
        return match.Success ? match.Groups["state"].Value : null;
    }

    [GeneratedRegex("(?:SQLSTATE[=:]\\s*|ERROR \\[)(?<state>[0-9A-Za-z]{5})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SqlStateFromMessageRegex();

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
