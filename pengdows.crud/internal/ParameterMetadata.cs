using System.Data;

namespace pengdows.crud.@internal;

/// <summary>
/// Lightweight struct for storing parameter metadata during SQL building.
/// Replaces storing heavy DbParameter objects until execution time.
/// Optimized for parameter pooling - stores just enough info to populate a pooled DbParameter.
/// </summary>
/// <remarks>
/// Size: ~16 bytes (2 object references + 2 enum values)
/// - Name: 8 bytes (reference)
/// - Value: 8 bytes (reference)
/// - DbType: 4 bytes (enum)
/// - Direction: 4 bytes (enum)
///
/// Compare to DbParameter: ~100+ bytes with property overhead
/// </remarks>
internal readonly struct ParameterMetadata
{
    /// <summary>
    /// Parameter name (without dialect-specific prefix like @ or :)
    /// </summary>
    public readonly string Name;

    /// <summary>
    /// Database type for the parameter
    /// </summary>
    public readonly DbType DbType;

    /// <summary>
    /// Parameter value (null allowed)
    /// </summary>
    public readonly object? Value;

    /// <summary>
    /// Parameter direction (Input, Output, InputOutput, ReturnValue)
    /// </summary>
    public readonly ParameterDirection Direction;

    /// <summary>
    /// Creates parameter metadata with all properties
    /// </summary>
    /// <param name="name">Parameter name</param>
    /// <param name="dbType">Database type</param>
    /// <param name="value">Parameter value (nullable)</param>
    /// <param name="direction">Parameter direction (defaults to Input)</param>
    public ParameterMetadata(
        string name,
        DbType dbType,
        object? value,
        ParameterDirection direction = ParameterDirection.Input)
    {
        Name = name;
        DbType = dbType;
        Value = value;
        Direction = direction;
    }

    /// <summary>
    /// True if this is an output, input/output, or return value parameter.
    /// Used to determine if we need to copy values back after execution.
    /// </summary>
    public bool IsOutput =>
        Direction == ParameterDirection.Output ||
        Direction == ParameterDirection.InputOutput ||
        Direction == ParameterDirection.ReturnValue;
}
