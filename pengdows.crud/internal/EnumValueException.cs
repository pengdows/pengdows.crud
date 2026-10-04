namespace pengdows.crud.@internal;

/// <summary>
/// A stored value that is no member of its enum property (REV-071). An <see cref="ArgumentException"/>, as
/// these failures always were, but its own type so the read paths report it as a conversion failure
/// naming the column instead of letting a bare ArgumentException escape.
/// </summary>
internal sealed class EnumValueException : ArgumentException
{
    public EnumValueException(string message) : base(message)
    {
    }
}
