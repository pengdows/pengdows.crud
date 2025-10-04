#region

using System.Data.Common;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.fakeDb;

/// <summary>
/// A fake connection string builder that supports provider-specific keys for testing
/// </summary>
public sealed class fakeDbConnectionStringBuilder : DbConnectionStringBuilder
{
    public fakeDbConnectionStringBuilder(SupportedDatabase database)
    {
        // Just use the base DbConnectionStringBuilder functionality
        // The base class handles provider-specific keys just fine
    }
}