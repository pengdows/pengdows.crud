using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DEC-013 (REL-007, measured live 2026-10-04): SqlClient allocates ~100 B/row of byte[] for
/// ReadAsync without CommandBehavior.SequentialAccess. Gateway hydration reads each column once in
/// ordinal order, so on SQL Server it opens its reader with SequentialAccess; readers handed to
/// callers keep Default. fakeDb enforces the rule as SqlClient does (no reading an earlier column).
/// </summary>
public class SequentialHydrationTests
{
    [Table("rows")]
    public class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
        [Column("score", DbType.Double)] public double Score { get; set; }
        [Column("payload", DbType.Binary)] public byte[]? Payload { get; set; }
    }

    private static List<Dictionary<string, object?>> Rows(int n) =>
        Enumerable.Range(1, n).Select(i => new Dictionary<string, object?>
        {
            ["id"] = i, ["name"] = "n" + i, ["score"] = i * 1.5, ["payload"] = new byte[] { (byte)i, 2, 3 }
        }).ToList();

    private static (DatabaseContext Context, fakeDbConnection Conn) Context(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        var conn = new fakeDbConnection { EmulatedProduct = db };
        factory.Connections.Add(conn);
        return (new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=x;EmulatedProduct={db}",
            DbMode = DbMode.SingleConnection
        }, factory), conn);
    }

    [Fact]
    public void FakeDbReader_WithSequentialAccess_RejectsReadingAnEarlierColumn()
    {
        var reader = new fakeDbDataReader(Rows(2).Select(r => r.ToDictionary(k => k.Key, k => k.Value!)))
            { EnforceSequentialAccess = true };
        Assert.True(reader.Read());

        Assert.False(reader.IsDBNull(1));
        Assert.Equal("n1", reader.GetString(1));
        Assert.Equal(1.5, reader.GetDouble(2));
        Assert.Throws<InvalidOperationException>(() => reader.GetInt32(0));

        Assert.True(reader.Read());
        Assert.Equal(2, reader.GetInt32(0));
    }

    [Fact]
    public async Task SqlServer_LoadListAsync_HydratesWithSequentialAccess()
    {
        var (context, conn) = Context(SupportedDatabase.SqlServer);
        await using var _ = context;
        conn.EnqueueReaderResult(Rows(3));
        var gateway = new TableGateway<Row, int>(context);
        await using var sc = gateway.BuildBaseRetrieve("r");

        var rows = await gateway.LoadListAsync(sc);

        Assert.Equal(3, rows.Count);
        Assert.Equal(new byte[] { 3, 2, 3 }, rows[2].Payload);
        Assert.True(conn.ExecutedReaderBehaviors.Last().HasFlag(CommandBehavior.SequentialAccess));
    }

    [Fact]
    public async Task SqlServer_LoadSingleAndStream_HydrateWithSequentialAccess()
    {
        var (context, conn) = Context(SupportedDatabase.SqlServer);
        await using var _ = context;
        var gateway = new TableGateway<Row, int>(context);

        conn.EnqueueReaderResult(Rows(1));
        await using (var sc = gateway.BuildBaseRetrieve("r"))
        {
            Assert.NotNull(await gateway.LoadSingleAsync(sc));
        }
        Assert.True(conn.ExecutedReaderBehaviors.Last().HasFlag(CommandBehavior.SequentialAccess));

        conn.EnqueueReaderResult(Rows(2));
        await using (var sc = gateway.BuildBaseRetrieve("r"))
        {
            var count = 0;
            await foreach (var row in gateway.LoadStreamAsync(sc))
            {
                count++;
            }

            Assert.Equal(2, count);
        }
        Assert.True(conn.ExecutedReaderBehaviors.Last().HasFlag(CommandBehavior.SequentialAccess));
    }

    [Fact]
    public async Task SqlServer_CallerReader_KeepsDefaultBehavior()
    {
        var (context, conn) = Context(SupportedDatabase.SqlServer);
        await using var _ = context;
        conn.EnqueueReaderResult(Rows(1));
        await using var sc = context.CreateSqlContainer("SELECT id, name, score, payload FROM rows");

        await using var reader = await sc.ExecuteReaderAsync();

        Assert.False(conn.ExecutedReaderBehaviors.Last().HasFlag(CommandBehavior.SequentialAccess));
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.Oracle)]
    public async Task OtherDatabases_LoadListAsync_KeepsDefaultBehavior(SupportedDatabase db)
    {
        var (context, conn) = Context(db);
        await using var _ = context;
        conn.EnqueueReaderResult(Rows(1));
        var gateway = new TableGateway<Row, int>(context);
        await using var sc = gateway.BuildBaseRetrieve("r");

        await gateway.LoadListAsync(sc);

        Assert.False(conn.ExecutedReaderBehaviors.Last().HasFlag(CommandBehavior.SequentialAccess));
    }

    // The failure path re-read columns to name the one that failed; under SequentialAccess that
    // re-read throws and would blame the wrong column. It still fails as DataMappingException.
    [Fact]
    public async Task SqlServer_UnreadableValue_StillFailsAsDataMappingException()
    {
        var (context, conn) = Context(SupportedDatabase.SqlServer);
        await using var _ = context;
        var rows = Rows(1);
        rows[0]["score"] = "not a number";
        conn.EnqueueReaderResult(rows);
        var gateway = new TableGateway<Row, int>(context);
        await using var sc = gateway.BuildBaseRetrieve("r");

        var ex = await Assert.ThrowsAsync<DataMappingException>(async () => await gateway.LoadListAsync(sc));

        // Named from the ordinal the mapper was reading, not by re-reading (which SequentialAccess
        // forbids): TYPE-008's "name the column" holds under sequential hydration too.
        Assert.Contains("'score'", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'id'", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'name'", ex.Message, StringComparison.Ordinal);
    }
}
