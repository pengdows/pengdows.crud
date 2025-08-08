using System;
using pengdows.crud.wrappers;

namespace pengdows.crud;

internal sealed class KeepAliveConnectionStrategy : StandardConnectionStrategy
{
    public KeepAliveConnectionStrategy(Func<ITrackedConnection> factory)
        : base(factory)
    {
    }
}

