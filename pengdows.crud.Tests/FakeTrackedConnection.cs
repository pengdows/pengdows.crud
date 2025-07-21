#region

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud.Tests;

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

        // Preload results for version queries
        if (connection is pengdows.crud.FakeDb.FakeDbConnection fake)
        {
            foreach (var value in scalars.Values)
            {
                // First for ExecuteReader based calls
                fake.EnqueueReaderResult(new[]
                {
                    new Dictionary<string, object> { { "version", value } }
                });

                // Then for ExecuteScalar based calls
                fake.EnqueueScalarResult(value);
            }
        }
    }

    DataTable ITrackedConnection.GetSchema(string dataSourceInformation) => _schema;

    DataTable ITrackedConnection.GetSchema() => _schema;
}