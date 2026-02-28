// =============================================================================
// FILE: ScalarResult.cs
// PURPOSE: Provides unambiguous scalar query result types that distinguish
//          between "no rows", "null value", and "actual value".
//
// AI SUMMARY:
// - ScalarStatus enum: None (no rows), Null (DBNull/null value), Value (non-null).
// - ScalarResult<T>: Readonly record struct wrapping status + value.
//   * HasValue: true only when Status == Value.
//   * Required: returns value or throws with clear message.
//   * Implicit conversion to T? for convenience.
// =============================================================================

namespace pengdows.crud;

/// <summary>
/// Describes the outcome of a scalar query execution.
/// </summary>
public enum ScalarStatus
{
    /// <summary>
    /// The query returned no rows.
    /// </summary>
    None,

    /// <summary>
    /// The query returned a row but the first column was <c>DBNull</c> or <c>null</c>.
    /// </summary>
    Null,

    /// <summary>
    /// The query returned a non-null value.
    /// </summary>
    Value
}

/// <summary>
/// Represents the result of a scalar query with explicit status semantics.
/// </summary>
/// <typeparam name="T">The expected value type.</typeparam>
/// <remarks>
/// <para>
/// Unlike ADO.NET's <c>DbCommand.ExecuteScalar()</c>, which conflates "no rows" with "null value"
/// (both return <c>null</c>), <see cref="ScalarResult{T}"/> provides unambiguous outcomes:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="ScalarStatus.None"/>: The query returned zero rows.</description></item>
///   <item><description><see cref="ScalarStatus.Null"/>: The query returned a row, but the value was <c>DBNull</c> or <c>null</c>.</description></item>
///   <item><description><see cref="ScalarStatus.Value"/>: The query returned a non-null value of type <typeparamref name="T"/>.</description></item>
/// </list>
/// </remarks>
public readonly record struct ScalarResult<T>(ScalarStatus Status, T? Value)
{
    /// <summary>
    /// Gets whether the result contains a non-null value.
    /// </summary>
    public bool HasValue => Status == ScalarStatus.Value;

    /// <summary>
    /// Gets the value, throwing if the query returned no rows or a null value.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="Status"/> is <see cref="ScalarStatus.None"/> or <see cref="ScalarStatus.Null"/>.
    /// </exception>
    public T Required => Status == ScalarStatus.Value
        ? Value!
        : throw new InvalidOperationException(
            Status == ScalarStatus.None
                ? "Query returned no rows."
                : "Query returned a null value.");
}