namespace pengdows.crud.@internal;

/// <summary>
/// Carries the ordinal the compiled mapper was reading when a value failed to convert, so the
/// gateway names that column without re-reading the row, which a sequential-access reader forbids
/// (DEC-013, TYPE-008). Never escapes the gateway: it is unwrapped into a DataMappingException.
/// </summary>
internal sealed class ColumnReadException : Exception
{
    public ColumnReadException(int ordinal, Exception inner)
        : base(inner.Message, inner)
    {
        Ordinal = ordinal;
    }

    public int Ordinal { get; }
}
