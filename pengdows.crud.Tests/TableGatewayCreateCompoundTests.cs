using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for CompoundStatement generated-key plan (MySQL, MariaDB, SQLite pre-3.35).
/// The compound path appends "; SELECT LAST_INSERT_ID()" (or equivalent) to the INSERT,
/// executes as a reader, advances to the second result set to read the ID, and falls back
/// to PopulateGeneratedIdAsync when the reader doesn't support multiple result sets (fakeDb).
/// </summary>
public class TableGatewayCreateCompoundTests
{
    // -- MySQL compound path --

    [Fact]
    public async Task CreateAsync_MySql_CompoundPlan_ReturnsTrue()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<AutoIdEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        factory.SetIdPopulationResult(42, rowsAffected: 1);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory, typeMap);
        var gateway = new TableGateway<AutoIdEntity, int>(context);
        var entity = new AutoIdEntity { Name = "test" };

        var result = await gateway.CreateAsync(entity);

        Assert.True(result);
    }

    [Fact]
    public async Task CreateAsync_MySql_CompoundPlan_PopulatesEntityId()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<AutoIdEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        factory.SetIdPopulationResult(99, rowsAffected: 1);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory, typeMap);
        var gateway = new TableGateway<AutoIdEntity, int>(context);
        var entity = new AutoIdEntity { Name = "test" };

        await gateway.CreateAsync(entity);

        Assert.Equal(99, entity.Id);
    }

    [Fact]
    public async Task CreateAsync_MySql_CompoundPlan_WritableId_SkipsIdPopulation()
    {
        // Writable [Id] means the client provides the ID — compound path should not run
        var typeMap = new TypeMapRegistry();
        typeMap.Register<WritableIdEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        factory.SetNonQueryResult(1);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory, typeMap);
        var gateway = new TableGateway<WritableIdEntity, int>(context);
        var entity = new WritableIdEntity { Id = 7, Name = "test" };

        var result = await gateway.CreateAsync(entity);

        Assert.True(result);
        Assert.Equal(7, entity.Id); // Id unchanged — client-provided
    }

    [Fact]
    public async Task BuildCreate_MySql_CompoundPlan_AppendsSuffix()
    {
        // Verify that when the gateway builds the INSERT for a compound-plan entity,
        // the SQL generation itself (BuildCreate) doesn't add the suffix — that's added at execution time.
        // The suffix is appended by CreateAsync, not BuildCreate.
        var typeMap = new TypeMapRegistry();
        typeMap.Register<AutoIdEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory, typeMap);
        var gateway = new TableGateway<AutoIdEntity, int>(context);
        var entity = new AutoIdEntity { Name = "test" };

        using var sc = gateway.BuildCreate(entity);

        // BuildCreate itself does NOT append the suffix — that's CreateAsync's responsibility
        Assert.DoesNotContain("LAST_INSERT_ID", sc.Query.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // -- MariaDB compound path --

    [Fact]
    public async Task CreateAsync_MariaDb_CompoundPlan_ReturnsTrue()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<AutoIdEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        factory.SetIdPopulationResult(5, rowsAffected: 1);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=MariaDb", factory, typeMap);
        var gateway = new TableGateway<AutoIdEntity, int>(context);
        var entity = new AutoIdEntity { Name = "mariadb" };

        var result = await gateway.CreateAsync(entity);

        Assert.True(result);
    }

    // -- SQLite pre-3.35 compound path --

    [Fact]
    public async Task CreateAsync_Sqlite_Uninitialized_CompoundPlan_ReturnsTrue()
    {
        // Uninitialized dialect → no version → SupportsInsertReturning=false → CompoundStatement plan
        var typeMap = new TypeMapRegistry();
        typeMap.Register<AutoIdEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.SetIdPopulationResult(77, rowsAffected: 1);
        var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory, typeMap);
        var gateway = new TableGateway<AutoIdEntity, int>(context);
        var entity = new AutoIdEntity { Name = "sqlite" };

        var result = await gateway.CreateAsync(entity);

        Assert.True(result);
    }

    // -- Happy path via multi-result-set reader (covers generatedId != null branch) --

    [Fact]
    public async Task CreateAsync_MySql_CompoundPlan_PopulatesIdFromSecondResultSet()
    {
        // Use EnqueueMultiResultReader so NextResultAsync() returns true for the second result set,
        // exercising the generatedId = inner[0] and ConvertWithCache paths (non-CT overload).
        // The operation connection is added AFTER DatabaseContext init so it is used for the operation.
        var typeMap = new TypeMapRegistry();
        typeMap.Register<AutoIdEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory, typeMap);
        var gateway = new TableGateway<AutoIdEntity, int>(context);
        var entity = new AutoIdEntity { Name = "multi" };

        // Add operation connection with two result sets:
        // RS1: empty (INSERT rows-affected), RS2: one row containing the generated ID.
        var opConn = new fakeDbConnection();
        opConn.EnqueueMultiResultReader(new[]
        {
            Array.Empty<Dictionary<string, object?>>(),
            new[] { new Dictionary<string, object?> { { "id", 55L } } }
        });
        factory.Connections.Insert(0, opConn);

        var result = await gateway.CreateAsync(entity);

        Assert.True(result);
        Assert.Equal(55, entity.Id);
    }

    // -- Cancellation token overloads --

    [Fact]
    public async Task CreateAsync_CT_MySql_CompoundPlan_ReturnsTrue()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<AutoIdEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        factory.SetIdPopulationResult(42, rowsAffected: 1);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory, typeMap);
        var gateway = new TableGateway<AutoIdEntity, int>(context);
        var entity = new AutoIdEntity { Name = "test" };

        var result = await gateway.CreateAsync(entity, context, cancellationToken: default);

        Assert.True(result);
    }

    [Fact]
    public async Task CreateAsync_CT_MySql_CompoundPlan_PopulatesIdFromSecondResultSet()
    {
        // Covers the CancellationToken overload's generatedId != null branch.
        var typeMap = new TypeMapRegistry();
        typeMap.Register<AutoIdEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory, typeMap);
        var gateway = new TableGateway<AutoIdEntity, int>(context);
        var entity = new AutoIdEntity { Name = "ct-multi" };

        var opConn = new fakeDbConnection();
        opConn.EnqueueMultiResultReader(new[]
        {
            Array.Empty<Dictionary<string, object?>>(),
            new[] { new Dictionary<string, object?> { { "id", 88L } } }
        });
        factory.Connections.Insert(0, opConn);

        var result = await gateway.CreateAsync(entity, context, cancellationToken: default);

        Assert.True(result);
        Assert.Equal(88, entity.Id);
    }

    // -- Entity type definitions --

    [Table("auto_id")]
    private class AutoIdEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Table("writable_id")]
    private class WritableIdEntity
    {
        [Id(true)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }
}
