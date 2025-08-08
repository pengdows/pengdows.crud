namespace pengdow.crud.exceptions;

public class PrimaryKeyOnRowIdColumn
    : Exception
{
    public PrimaryKeyOnRowIdColumn(string message) : base(message)
    {
    }
}