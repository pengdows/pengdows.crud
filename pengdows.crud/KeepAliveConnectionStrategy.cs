using System;

namespace pengdows.crud;

internal sealed class KeepAliveConnectionStrategy : StandardConnectionStrategy
{
    public KeepAliveConnectionStrategy(Func<ITrackedConnection> factory)
        : base(factory)
    {
    }
}

