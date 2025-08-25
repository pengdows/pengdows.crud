using System.Data.Common;
using pengdows.crud.FakeDb;
using pengdows.crud.enums;

namespace pengdows.crud.Tests.Mocks;

internal sealed class NullParameterFactory : DbProviderFactory
{
    private readonly FakeDbFactory _inner = new FakeDbFactory(SupportedDatabase.Sqlite);

    public override DbConnection CreateConnection() => _inner.CreateConnection();
    public override DbCommand CreateCommand() => _inner.CreateCommand();
    public override DbParameter CreateParameter() => null!;
}
