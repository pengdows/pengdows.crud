using System.Data.Common;
using pengdows.crud.enums;

namespace pengdows.crud.Tests.Mocks;

internal sealed class NullParameterFactory : DbProviderFactory
{
    private readonly fakeDbFactory _inner = new(SupportedDatabase.Sqlite);

    public override DbConnection CreateConnection()
    {
        return _inner.CreateConnection();
    }

    public override DbCommand CreateCommand()
    {
        return _inner.CreateCommand();
    }

    public override DbParameter CreateParameter()
    {
        return null!;
    }
}