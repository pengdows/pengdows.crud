using System;
using pengdow.crud.wrappers;

namespace pengdow.crud;

internal sealed class KeepAliveConnectionStrategy : StandardConnectionStrategy
{
    public KeepAliveConnectionStrategy(Func<ITrackedConnection> factory)
        : base(factory)
    {
    }
}

