using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// TEST-010: generated-ID connection affinity. CORE-016 already traced PopulateGeneratedIdAsync's
// fallback path (reached whenever the CompoundStatement plan's own single-round-trip read fails
// to navigate to its trailing result set — always true against fakeDb, since
// fakeDbDataReader.NextResult() unconditionally returns false) and flagged it as re-obtaining a
// connection with no guaranteed affinity to the INSERT's own connection, but stopped short of a
// fix pending real-provider verification for the full "same physical connection" restructuring.
//
// This test isolates one concrete, narrower defect found while investigating that fallback more
// closely: it fetches the generated ID via `ExecuteScalarOrNullAsync<object>(CommandType.Text, ct)`,
// whose parameterless-ExecutionType overload hardcodes `ExecutionType.Read` (see
// SqlContainer.ExecuteScalarOrNullAsync<T>(CommandType, CancellationToken)). A session-scoped
// last-insert-id function (MySQL's LAST_INSERT_ID(), SQLite's last_insert_rowid(), etc.) is tied
// to the connection that ran the INSERT, which acquired an ExecutionType.Write connection — using
// Read here sends the follow-up query down the READ pool/connection-string instead, which on any
// real provider with a distinct read path returns NULL/0/stale data, not a "maybe less optimal but
// still correct" choice. This is independently wrong regardless of the broader same-connection
// architecture question CORE-016 left open, and is fixable without that larger restructuring.
public class GeneratedIdConnectionAffinityTests
{
    [Table("gen_id_items")]
    private class GenIdItem
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task CreateAsync_CompoundStatementFallback_NeverUsesAReadConnectionForTheGeneratedIdQuery()
    {
        // MySql (not MySqlConnector — fakeDbFactory's namespace never contains that substring)
        // resolves to GeneratedKeyPlan.CompoundStatement, and fakeDbDataReader.NextResult()
        // always returning false forces every CreateAsync through PopulateGeneratedIdAsync's
        // fallback, exactly like a real MySql.Data provider would if its own multi-result
        // navigation ever failed.
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var typeMap = new TypeMapRegistry();
        typeMap.Register<GenIdItem>();

        using var ctx = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=gen-id-affinity;EmulatedProduct=MySql"
        }, factory, null, typeMap);

        var gateway = new TableGateway<GenIdItem, int>(ctx);
        var entity = new GenIdItem { Name = "affinity-check" };

        var created = await gateway.CreateAsync(entity);
        Assert.True(created);

        // A read-labeled connection carries the dialect's read-only session settings
        // ("transaction_read_only = 1" for MySQL); a write-labeled one carries "= 0". No
        // connection used anywhere in this CreateAsync call should be read-labeled — the whole
        // operation (insert, and fetching the ID it produced) is a write concern end to end.
        var readLabeledConnectionWasUsed = factory.CreatedConnections.Any(conn =>
            conn.ExecutedNonQueryTexts.Any(text => text.Contains("transaction_read_only = 1")));

        Assert.False(readLabeledConnectionWasUsed,
            "PopulateGeneratedIdAsync's fallback must fetch the generated ID through a write-labeled " +
            "connection, not a read-labeled one — a session-scoped last-insert-id function has no " +
            "meaningful value on a connection acquired for reads.");
    }

    // TEST-010's remaining half, CORE-016 (fixed 2026-09-29): the fallback id query must run on the
    // same physical connection as the INSERT. It used to open a new lease, which on a real provider
    // reads another session's value, a pooled connection's stale value, or 0. fakeDb records executed
    // text per connection instance, so affinity is provable. Every plan whose fallback follows the
    // INSERT is covered: CompoundStatement (MySql.Data), ReaderInsertedId (MySqlConnector) and
    // Returning (PostgreSQL; the fallback runs when RETURNING yields no row).
    public static IEnumerable<object[]> FallbackPlans()
    {
        yield return new object[] { "CompoundStatement" };
        yield return new object[] { "ReaderInsertedId" };
        yield return new object[] { "Returning" };
    }

    [Theory]
    [MemberData(nameof(FallbackPlans))]
    public async Task CreateAsync_GeneratedIdFallback_UsesTheSamePhysicalConnectionAsTheInsert(string plan)
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<GenIdItem>();
        var database = plan == "Returning" ? SupportedDatabase.PostgreSql : SupportedDatabase.MySql;
        var factory = new fakeDbFactory(database);
        using var ctx = plan == "ReaderInsertedId"
            ? new DatabaseContext("Data Source=gen-id-affinity-2;EmulatedProduct=MySql", factory, typeMap,
                new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: true))
            : new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = $"Data Source=gen-id-affinity-2;EmulatedProduct={database}"
            }, factory, null, typeMap);
        Assert.Equal(plan, ctx.Dialect.GetGeneratedKeyPlan().ToString());
        var idQuery = ctx.Dialect.GetLastInsertedIdQuery();

        var gateway = new TableGateway<GenIdItem, int>(ctx);
        Assert.True(await gateway.CreateAsync(new GenIdItem { Name = "same-connection-check" }));

        static IEnumerable<string> Executed(fakeDbConnection c) =>
            c.ExecutedNonQueryTexts.Concat(c.ExecutedReaderTexts).Concat(c.ExecutedScalarTexts);
        var insertConnections = factory.CreatedConnections
            .Where(c => Executed(c).Any(t => t.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var idConnections = factory.CreatedConnections
            .Where(c => Executed(c).Any(t => t.Trim() == idQuery.Trim()))
            .ToList();

        var insertConnection = Assert.Single(insertConnections);
        var idConnection = Assert.Single(idConnections);
        Assert.Same(insertConnection, idConnection);
    }

    // Under SingleWriter the pinned connection carries the one write permit; the fallback id query
    // runs on it and must not wait for a second permit (which would never come).
    [Fact]
    public async Task CreateAsync_SingleWriter_FallbackRunsOnThePinnedConnectionWithoutASecondPermit()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<GenIdItem>();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=gen-id-single-writer.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter
        }, factory, null, typeMap);
        Assert.Equal(DbMode.SingleWriter, ctx.ConnectionMode);
        var idQuery = ctx.Dialect.GetLastInsertedIdQuery();
        var gateway = new TableGateway<GenIdItem, int>(ctx);

        var create = gateway.CreateAsync(new GenIdItem { Name = "single-writer" }).AsTask();
        var finished = await Task.WhenAny(create, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(create, finished);
        Assert.True(await create);
        static IEnumerable<string> Executed(fakeDbConnection c) =>
            c.ExecutedNonQueryTexts.Concat(c.ExecutedReaderTexts).Concat(c.ExecutedScalarTexts);
        var insertConnection = Assert.Single(factory.CreatedConnections,
            c => Executed(c).Any(t => t.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)));
        var idConnection = Assert.Single(factory.CreatedConnections, c => Executed(c).Any(t => t.Trim() == idQuery.Trim()));
        Assert.Same(insertConnection, idConnection);
    }
}
