using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// The base dialect's command hooks do nothing: only Informix, SAP HANA and Snowflake prepare
/// commands, and only Informix carries a non-key column in its MERGE source.
/// </summary>
public class SqlDialectCommandHookDefaultsTests
{
    [Table("t")]
    public class Row
    {
        [Id] [Column("id", System.Data.DbType.Int32)] public int Id { get; set; }
        [Column("v", System.Data.DbType.String)] public string? V { get; set; }
    }

    [Fact]
    public async Task BaseDialect_DoesNotPrepareCommands_OrSourceMergeColumns()
    {
        await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", new fakeDbFactory(SupportedDatabase.Sqlite));
        var dialect = (SqlDialect)context.Dialect;
        var column = pengdows.crud.@internal.DatabaseContextTypeMapExtensions.GetInternalTypeMapRegistry(context)
            .GetTableInfo<Row>().Columns["v"];
        using var command = new fakeDbCommand { CommandText = "SELECT 1" };

        await dialect.PrepareCommandAsync(command, default);

        Assert.False(dialect.PreparesCommands);
        Assert.Equal("SELECT 1", command.CommandText);
        Assert.False(dialect.MergeSourcesColumn(column));
    }
}
