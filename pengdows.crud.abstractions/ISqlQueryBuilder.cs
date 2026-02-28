namespace pengdows.crud;

/// <summary>
/// High-performance, pooled SQL query builder optimized for repeated appends.
/// Replaces <see cref="System.Text.StringBuilder"/> for SQL construction with
/// zero-allocation <see cref="ReadOnlySpan{T}"/> appends.
/// </summary>
public interface ISqlQueryBuilder : IDisposable
{
    /// <summary>
    /// Current length of the query text.
    /// </summary>
    int Length { get; }

    /// <summary>
    /// Mutation version for cache invalidation.
    /// </summary>
    int Version { get; }

    /// <summary>Appends a single character.</summary>
    ISqlQueryBuilder Append(char value);

    /// <summary>Appends a string.</summary>
    ISqlQueryBuilder Append(string? value);

    /// <summary>Appends a span of characters.</summary>
    ISqlQueryBuilder Append(ReadOnlySpan<char> value);

    /// <summary>Appends another query builder's content.</summary>
    ISqlQueryBuilder Append(ISqlQueryBuilder other);

    /// <summary>Appends an integer formatted with the current culture.</summary>
    ISqlQueryBuilder Append(int value);

    /// <summary>Appends a long formatted with the current culture.</summary>
    ISqlQueryBuilder Append(long value);

    /// <summary>Appends a double formatted with the current culture.</summary>
    ISqlQueryBuilder Append(double value);

    /// <summary>Appends a decimal formatted with the current culture.</summary>
    ISqlQueryBuilder Append(decimal value);

    /// <summary>Appends an object's string representation.</summary>
    ISqlQueryBuilder Append(object? value);

    /// <summary>Appends a newline character.</summary>
    ISqlQueryBuilder AppendLine();

    /// <summary>Appends a string followed by a newline character.</summary>
    ISqlQueryBuilder AppendLine(string? value);

    /// <summary>Appends a formatted string using the current culture.</summary>
    ISqlQueryBuilder AppendFormat(string format, params object?[] args);

    /// <summary>Appends a formatted string using the specified format provider.</summary>
    ISqlQueryBuilder AppendFormat(IFormatProvider? provider, string format, params object?[] args);

    /// <summary>Replaces all occurrences of a string within the builder.</summary>
    ISqlQueryBuilder Replace(string oldValue, string? newValue);

    /// <summary>Clears the builder content without releasing the buffer.</summary>
    ISqlQueryBuilder Clear();

    /// <summary>Returns the built SQL string.</summary>
    string ToString();
}