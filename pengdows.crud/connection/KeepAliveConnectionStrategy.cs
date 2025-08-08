using System;
using pengdows.crud.wrappers;

namespace pengdows.crud.connection;

internal sealed class KeepAliveConnectionStrategy : StandardConnectionStrategy
{
    public KeepAliveConnectionStrategy(Func<ITrackedConnection> factory)
        : base(factory)
    {
    }
}

