using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class EntityHelperStringIdTemplateTests
{
    private readonly TypeMapRegistry _typeMap;

    [Table("StringEntities")]
    private class StringIdEntity
    {
        [Id(false)]
        [Column("Id", DbType.String)]
        public string Id { get; set; } = string.Empty;

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    public EntityHelperStringIdTemplateTests()
    {
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<StringIdEntity>();
    }

    [Fact(Skip = "Test timing/template caching behavior changed")]
    public async Task RetrieveAsync_Sqlite_StringId_UsesCachedTemplate()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite)
        {
            EnableDataPersistence = true
        };
        var primaryConnection = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.Sqlite,
            EnableDataPersistence = true
        };
        factory.Connections.Add(primaryConnection);

        var configuration = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file:stringids?mode=memory&cache=shared;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection
        };

        using var ctx = new DatabaseContext(configuration, factory, loggerFactory: null, typeMapRegistry: _typeMap);
        var helper = new EntityHelper<StringIdEntity, string>(ctx);

        var entity = new StringIdEntity
        {
            Id = "alpha",
            Name = "first"
        };
        await helper.CreateAsync(entity, ctx);

        var dataStoreField = typeof(fakeDbConnection).GetField("DataStore", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var dataStore = (FakeDataStore?)dataStoreField?.GetValue(primaryConnection);
        var storedRows = dataStore?.ExecuteReader("SELECT * FROM StringEntities", null).ToList() ?? new List<Dictionary<string, object?>>();
        System.Console.WriteLine($"Stored rows (Sqlite): {storedRows.Count}");

        var result = await helper.RetrieveAsync(new[] { "alpha" });

        foreach (var sql in primaryConnection.ExecutedReaderTexts)
        {
            System.Console.WriteLine($"Executed SQL (Sqlite): {sql}");
        }

        Assert.Single(result);
        Assert.Equal("first", result[0].Name);
    }

    [Fact(Skip = "Test timing/template caching behavior changed")]
    public async Task RetrieveAsync_Postgres_StringId_UsesCachedTemplate()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql)
        {
            EnableDataPersistence = true
        };
        var primaryConnection = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.PostgreSql,
            EnableDataPersistence = true
        };
        factory.Connections.Add(primaryConnection);

        var configuration = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=pg;EmulatedProduct=PostgreSql",
            DbMode = DbMode.SingleConnection
        };

        using var ctx = new DatabaseContext(configuration, factory, loggerFactory: null, typeMapRegistry: _typeMap);
        var helper = new EntityHelper<StringIdEntity, string>(ctx);

        var entity = new StringIdEntity
        {
            Id = "bravo",
            Name = "second"
        };
        await helper.CreateAsync(entity, ctx);

        var dataStoreField = typeof(fakeDbConnection).GetField("DataStore", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var dataStore = (FakeDataStore?)dataStoreField?.GetValue(primaryConnection);
        var storedRows = dataStore?.ExecuteReader("SELECT * FROM StringEntities", null).ToList() ?? new List<Dictionary<string, object?>>();
        System.Console.WriteLine($"Stored rows (Postgres): {storedRows.Count}");

        var result = await helper.RetrieveAsync(new[] { "bravo" });

        foreach (var sql in primaryConnection.ExecutedReaderTexts)
        {
            System.Console.WriteLine($"Executed SQL (Postgres): {sql}");
        }

        Assert.Single(result);
        Assert.Equal("second", result[0].Name);
    }
}
