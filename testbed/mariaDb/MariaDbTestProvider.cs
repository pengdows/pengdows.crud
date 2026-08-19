using System.Data;
using pengdows.crud;
using pengdows.crud.attributes;

namespace testbed.mariaDb;

public sealed class MariaDbTestProvider : TestProvider
{
    public MariaDbTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    protected override async Task RunAdditionalTestsAsync()
    {
        await TestUnsignedIdentityAsync<UIntIdentityRow, uint>(
            "mariadb_uint_identity_test",
            "INT UNSIGNED",
            2_147_483_648UL);
        await TestUnsignedIdentityAsync<ULongIdentityRow, ulong>(
            "mariadb_ulong_identity_test",
            "BIGINT UNSIGNED",
            9_223_372_036_854_775_808UL);
    }

    private async Task TestUnsignedIdentityAsync<TEntity, TId>(string table, string idType, ulong firstIdentity)
        where TEntity : class, new()
    {
        var tableName = _context.WrapObjectName(table);
        await using var container = _context.CreateSqlContainer();
        container.Query.Append($"DROP TABLE IF EXISTS {tableName}");
        await container.ExecuteNonQueryAsync();

        try
        {
            container.Clear();
            container.Query.Append($"CREATE TABLE {tableName} (");
            container.Query.Append($"{_context.WrapObjectName("id")} {idType} AUTO_INCREMENT PRIMARY KEY, ");
            container.Query.Append($"{_context.WrapObjectName("value")} VARCHAR(100) NOT NULL) ");
            container.Query.Append($"AUTO_INCREMENT={firstIdentity}");
            await container.ExecuteNonQueryAsync();

            var gateway = new TableGateway<TEntity, TId>(_context);
            var row = new TEntity();
            typeof(TEntity).GetProperty("Value")!.SetValue(row, "unsigned identity");
            var created = await gateway.CreateAsync(row);
            var id = (TId)typeof(TEntity).GetProperty("Id")!.GetValue(row)!;
            if (!created || Convert.ToUInt64(id) < firstIdentity)
            {
                throw new Exception($"MariaDB {idType} identity was not returned as {typeof(TId).Name}");
            }

            CheckOk($"MariaDB {idType} identity returned as {typeof(TId).Name}: OK");
        }
        finally
        {
            container.Clear();
            container.Query.Append($"DROP TABLE IF EXISTS {tableName}");
            await container.ExecuteNonQueryAsync();
        }
    }

    [Table("mariadb_uint_identity_test")]
    private class UIntIdentityRow
    {
        [Id(false)]
        [Column("id", DbType.UInt32)]
        public uint Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }

    [Table("mariadb_ulong_identity_test")]
    private class ULongIdentityRow
    {
        [Id(false)]
        [Column("id", DbType.UInt64)]
        public ulong Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }
}
