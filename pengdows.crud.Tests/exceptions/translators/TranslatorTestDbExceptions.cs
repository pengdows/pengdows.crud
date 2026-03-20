using System.Data.Common;

namespace pengdows.crud.Tests.exceptions.translators;

internal sealed class NumberedDbException : DbException
{
    public NumberedDbException(int number, string message)
        : base(message)
    {
        Number = number;
    }

    public int Number { get; }
}

internal sealed class SqlStateDbException : DbException
{
    public SqlStateDbException(string sqlState, string message)
        : base(message)
    {
        SqlState = sqlState;
    }

    public override string? SqlState { get; }
}

internal sealed class SqliteMessageDbException : DbException
{
    public SqliteMessageDbException(string message)
        : base(message)
    {
    }
}
