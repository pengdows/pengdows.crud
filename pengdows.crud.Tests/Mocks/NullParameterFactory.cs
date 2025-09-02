using System.Data.Common;
using pengdows.crud.fakeDb;
using pengdows.crud.enums;

namespace pengdows.crud.Tests.Mocks;

internal sealed class NullParameterFactory : DbProviderFactory
{
    private readonly fakeDbFactory _inner = new fakeDbFactory(SupportedDatabase.Sqlite);

    public override DbConnection CreateConnection() => _inner.CreateConnection();
    public override DbCommand CreateCommand() => _inner.CreateCommand();
    public override DbParameter CreateParameter() => null!;
}
