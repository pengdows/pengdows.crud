using pengdows.crud.enums;

namespace pengdows.crud;

internal static class InternalConnectionAccessAssertions
{
    internal static void AssertIsReadConnection(this IDatabaseContext context)
    {
        if ((context.ReadWriteMode & ReadWriteMode.ReadOnly) == 0)
        {
            throw new InvalidOperationException("The connection is not read connection.");
        }
    }

    internal static void AssertIsWriteConnection(this IDatabaseContext context)
    {
        if ((context.ReadWriteMode & ReadWriteMode.WriteOnly) == 0)
        {
            throw new InvalidOperationException("The connection is not write connection.");
        }

        if (context.IsReadOnlyConnection)
        {
            throw new InvalidOperationException("Transaction is read-only.");
        }
    }
}
