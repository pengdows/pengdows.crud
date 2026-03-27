using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for the ReaderInsertedId generated-key plan (MySqlConnector path).
/// MySqlConnector deliberately omits AllowMultipleStatements, so the CompoundStatement plan
/// cannot be used. Instead, the INSERT is executed as a reader and LastInsertedId is read
/// from the underlying command (populated from the MySQL OK packet) via reflection.
/// When LastInsertedId is null (fakeDb default), the code falls back to PopulateGeneratedIdAsync.
/// </summary>
public class TableGatewayCreateReaderInsertedIdTests
{
    [Table("auto_id")]
    private class AutoIdEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    private static (fakeDbFactory factory, DatabaseContext context, TableGateway<AutoIdEntity, int> gateway)
        CreateSetup(int generatedId = 42)
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<AutoIdEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        factory.SetIdPopulationResult(generatedId, rowsAffected: 1);

        // Use the internal dialect constructor so isMySqlConnector=true
        // forces GeneratedKeyPlan.ReaderInsertedId without needing a real MySqlConnector factory.
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: true);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory, typeMap, dialect);
        var gateway = new TableGateway<AutoIdEntity, int>(context);
        return (factory, context, gateway);
    }

    private static (fakeDbFactory factory, DatabaseContext context, TableGateway<AutoIdEntity, int> gateway)
        CreateSetupWithLastInsertedId(long lastInsertedId)
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<AutoIdEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        factory.SetIdPopulationResult(0, rowsAffected: 1);

        // Pre-seed every connection so the INSERT reader command reports LastInsertedId.
        foreach (var conn in factory.Connections)
        {
            conn.NextCommandLastInsertedId = lastInsertedId;
        }

        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: true);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory, typeMap, dialect);
        var gateway = new TableGateway<AutoIdEntity, int>(context);
        return (factory, context, gateway);
    }

    // -- Fallback path (LastInsertedId null → SELECT LAST_INSERT_ID()) --

    [Fact]
    public async Task CreateAsync_ReaderInsertedId_FallsBackToPopulate_WhenLastInsertedIdIsNull()
    {
        // fakeDbCommand.LastInsertedId defaults to null → reflection returns null →
        // PopulateGeneratedIdAsync is called → reads LAST_INSERT_ID() = 42
        var (_, _, gateway) = CreateSetup(generatedId: 42);
        var entity = new AutoIdEntity { Name = "test" };

        var result = await gateway.CreateAsync(entity);

        Assert.True(result);
        Assert.Equal(42, entity.Id);
    }

    // -- Happy path (LastInsertedId populated from OK packet) --

    [Fact]
    public async Task CreateAsync_ReaderInsertedId_SetsEntityId_WhenLastInsertedIdIsPopulated()
    {
        // NextCommandLastInsertedId = 99 → command.LastInsertedId = 99 →
        // GetLastInsertedIdFromCommand returns 99 → entity.Id = 99 (no fallback query)
        var (_, _, gateway) = CreateSetupWithLastInsertedId(lastInsertedId: 99L);
        var entity = new AutoIdEntity { Name = "ok-packet" };

        var result = await gateway.CreateAsync(entity);

        Assert.True(result);
        Assert.Equal(99, entity.Id);
    }

    // -- CancellationToken overload (lines 400-425) --

    [Fact]
    public async Task CreateAsync_CT_ReaderInsertedId_FallsBackToPopulate_WhenLastInsertedIdIsNull()
    {
        var (_, context, gateway) = CreateSetup(generatedId: 55);
        var entity = new AutoIdEntity { Name = "ct-fallback" };

        var result = await gateway.CreateAsync(entity, context, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(55, entity.Id);
    }

    [Fact]
    public async Task CreateAsync_CT_ReaderInsertedId_SetsEntityId_WhenLastInsertedIdIsPopulated()
    {
        var (_, context, gateway) = CreateSetupWithLastInsertedId(lastInsertedId: 88L);
        var entity = new AutoIdEntity { Name = "ct-ok-packet" };

        var result = await gateway.CreateAsync(entity, context, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(88, entity.Id);
    }
}
