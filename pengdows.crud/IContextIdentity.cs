using System;

namespace pengdows.crud;

public interface IContextIdentity
{
    Guid RootId { get; }
}
