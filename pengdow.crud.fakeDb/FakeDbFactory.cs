#region

using System.Data.Common;
using pengdow.crud.enums;

#endregion

namespace pengdow.crud.FakeDb;

public sealed class FakeDbFactory : DbProviderFactory
{
    public static readonly FakeDbFactory Instance = new();
    private readonly SupportedDatabase _pretendToBe;

    private FakeDbFactory()
    {
        _pretendToBe = SupportedDatabase.Unknown;
    }

    public FakeDbFactory(string pretendToBe)
    {
        _pretendToBe = Enum.Parse<SupportedDatabase>(pretendToBe);
    }

    public FakeDbFactory(SupportedDatabase pretendToBe)
    {
        _pretendToBe = pretendToBe;
    }

    public override DbCommand CreateCommand()
    {
        return new FakeDbCommand();
    }

    public override DbConnection CreateConnection()
    {
        var c = new FakeDbConnection();
        c.EmulatedProduct = _pretendToBe;
        return c;
    }

    public override DbParameter CreateParameter()
    {
        return new FakeDbParameter();
    }
}