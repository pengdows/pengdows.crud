#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

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
    ThrowOnIndexerSet = 1 << 2,

    /// <summary>
    /// Emulates typed provider builders (SqlConnectionStringBuilder, IBM's DB2ConnectionStringBuilder)
    /// whose ContainsKey reports every keyword the provider knows as present, set or not.
    /// </summary>
    ReportKnownKeywordsAsPresent = 1 << 3,

    /// <summary>
    /// Emulates typed provider builders that rewrite credential synonyms to their canonical
    /// keywords (IBM.Data.Db2: UID -> User ID, PWD -> Password; confirmed live), so the builder's
    /// ConnectionString no longer contains the keys the caller wrote.
    /// </summary>
    CanonicalizeCredentialKeywords = 1 << 4
}

/// <summary>
/// A fake connection string builder that supports provider-specific keys for testing
/// </summary>
public sealed class fakeDbConnectionStringBuilder : DbConnectionStringBuilder
{
    private readonly ConnectionStringBuilderBehavior _behavior;
    private readonly IReadOnlyCollection<string> _knownKeywords;

    internal fakeDbConnectionStringBuilder(SupportedDatabase database,
        ConnectionStringBuilderBehavior behavior = ConnectionStringBuilderBehavior.None,
        IReadOnlyCollection<string>? knownKeywords = null)
    {
        _behavior = behavior;
        _knownKeywords = knownKeywords ?? Array.Empty<string>();
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

            if (_behavior.HasFlag(ConnectionStringBuilderBehavior.CanonicalizeCredentialKeywords))
            {
                keyword = keyword.ToUpperInvariant() switch
                {
                    "UID" => "User ID",
                    "PWD" => "Password",
                    _ => keyword
                };
            }

            base[keyword] = value;
        }
    }
#nullable restore

    public override bool ContainsKey(string keyword)
    {
        if (base.ContainsKey(keyword))
        {
            return true;
        }

        return _behavior.HasFlag(ConnectionStringBuilderBehavior.ReportKnownKeywordsAsPresent) &&
               _knownKeywords.Contains(keyword, StringComparer.OrdinalIgnoreCase);
    }
}