#region

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud.Tests;

public class FakeTrackedConnection : TrackedConnection
{
    public FakeTrackedConnection(DbConnection connection, DataTable schema, Dictionary<string, object> scalars) :
        base(connection)
    {
        //   throw new NotImplementedException();
    }
}