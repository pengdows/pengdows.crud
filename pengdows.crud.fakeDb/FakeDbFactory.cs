#region

using System;
using System.Data.Common;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.FakeDb;

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
        if (!Enum.TryParse<SupportedDatabase>(pretendToBe, true, out _pretendToBe))
        {
            _pretendToBe = pretendToBe switch
            {
                "Postgres" => SupportedDatabase.PostgreSql,
                _ => throw new ArgumentException($"Requested value '{pretendToBe}' was not found.", nameof(pretendToBe))
            };
        }
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