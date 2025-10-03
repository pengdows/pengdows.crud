#region

using System.Data.Common;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.fakeDb;

public interface IFakeDbFactory
{
    SupportedDatabase PretendToBe { get; }

    IFakeDbConnection CreateConnection();

    DbParameter CreateParameter();
}
