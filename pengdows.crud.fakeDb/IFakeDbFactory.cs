#region

using System.Data.Common;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.fakeDb;

/// <summary>
/// Factory for creating fake database objects for tests.
/// </summary>
public interface IFakeDbFactory
{
    /// <summary>
    /// Database product this factory should emulate.
    /// </summary>
    SupportedDatabase PretendToBe { get; }

    /// <summary>
    /// Creates a new fake connection instance.
    /// </summary>
    IFakeDbConnection CreateConnection();

    /// <summary>
    /// Creates a new fake parameter instance.
    /// </summary>
    DbParameter CreateParameter();
}
