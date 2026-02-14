#region

using System;
using System.Data.Common;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.fakeDb;

/// <summary>
/// Configures fake connection string builder behavior for tests.
/// </summary>
[Flags]
internal enum ConnectionStringBuilderBehavior
{
    None = 0,
    ReturnNull = 1 << 0,
    ThrowOnConnectionStringSet = 1 << 1,
    ThrowOnIndexerSet = 1 << 2
}

/// <summary>
/// A fake connection string builder that supports provider-specific keys for testing
/// </summary>
public sealed class fakeDbConnectionStringBuilder : DbConnectionStringBuilder
{
    private readonly ConnectionStringBuilderBehavior _behavior;

    internal fakeDbConnectionStringBuilder(SupportedDatabase database,
        ConnectionStringBuilderBehavior behavior = ConnectionStringBuilderBehavior.None)
    {
        _behavior = behavior;
        // Just use the base DbConnectionStringBuilder functionality
        // The base class handles provider-specific keys just fine
    }

#nullable disable
    public override object this[string keyword]
    {
        get => base[keyword];
        set
        {
            if (_behavior.HasFlag(ConnectionStringBuilderBehavior.ThrowOnIndexerSet) ||
                _behavior.HasFlag(ConnectionStringBuilderBehavior.ThrowOnConnectionStringSet))
            {
                throw new InvalidOperationException("Indexer set failed.");
            }

            base[keyword] = value;
        }
    }
#nullable restore
}
