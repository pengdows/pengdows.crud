#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using pengdow.crud.wrappers;

#endregion

namespace pengdow.crud.Tests;

/// <summary>
/// Test wrapper that allows pre-configuring schema and scalar results for
/// <see cref="DataSourceInformation"/> tests.
/// </summary>
public class FakeTrackedConnection : TrackedConnection, ITrackedConnection
{
    private readonly DataTable _schema;

    public FakeTrackedConnection(
        DbConnection connection,
        DataTable schema,
        Dictionary<string, object> scalars) : base(connection)
    {
        _schema = schema;

        if (connection is pengdow.crud.FakeDb.FakeDbConnection fake && scalars.Count > 0)
        {
            var value = scalars.Values.First();
            var isSqlite = scalars.Keys.Any(k => k.Equals("SELECT sqlite_version()", StringComparison.OrdinalIgnoreCase));

            // First reader result is consumed by IsSqliteAsync. Queue an empty
            // result for non-SQLite databases so the actual version query result
            // remains intact.
            if (isSqlite)
            {
                fake.EnqueueReaderResult(new[]
                {
                    new Dictionary<string, object> { { "version", value } }
                });
            }
            else
            {
                fake.EnqueueReaderResult(Array.Empty<Dictionary<string, object>>());
            }

            // Result for version query
            fake.EnqueueReaderResult(new[]
            {
                new Dictionary<string, object> { { "version", value } }
            });

            // Result for ExecuteScalar based calls
            fake.EnqueueScalarResult(value);
        }
    }

    DataTable ITrackedConnection.GetSchema(string dataSourceInformation) => _schema;

    DataTable ITrackedConnection.GetSchema() => _schema;
}