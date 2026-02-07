#region

using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TableGatewayUpsertTests
{
    [Fact]
    public async Task BuildUpsert_OnConflict_UsesPrimaryKeyAndVersion()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=PostgreSql",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        await using var context = new DatabaseContext(cfg, factory);
        var helper = new TableGateway<ConflictEntity, long>(context,
            logger: NullLogger<TableGateway<ConflictEntity, long>>.Instance);

        var entity = new ConflictEntity
        {
            Id = 13,
            ExternalKey = "KEY",
            Value = "v1"
        };

        using var container = helper.BuildUpsert(entity, context);
        var sql = container.Query.ToString();

        Assert.Contains("\"version\" = \"version\" + 1", sql);
        Assert.True(sql.Contains("ON CONFLICT") || sql.Contains("MERGE INTO"),
            "Expected Postgres upsert to use ON CONFLICT or MERGE.");
        Assert.Equal(1, entity.Version);
    }

    [Table("upsert_entities")]
    private class ConflictEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;

        [PrimaryKey(1)]
        [Column("external_key", DbType.String)]
        public string ExternalKey { get; set; } = string.Empty;

        [Version]
        [Column("version", DbType.Int32)]
        public int Version { get; set; }
    }
}
