#region

using System;

#endregion

namespace pengdows.crud.exceptions;

public class TransactionModeNotSupportedException : NotSupportedException
{
    public TransactionModeNotSupportedException(string message)
        : base(message)
    {
    }
}
