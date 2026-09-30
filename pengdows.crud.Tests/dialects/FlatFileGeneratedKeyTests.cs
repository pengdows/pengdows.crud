using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// GEN-001 (FlatFile): CreateAsync returned true but left a database-generated id at 0. FlatFile has
/// no IDENTITY, RETURNING or session last-id function, but it has ISO sequences: a key column defaults
/// from NEXT VALUE FOR a sequence, and the value can be fetched first (VALUES (NEXT VALUE FOR "seq"),
/// confirmed against pengdows.flatfile 0.2.1-preview.2) and sent in the INSERT, the same
/// PrefetchSequence plan InterBase uses, with the gateway's "table_seq" naming.
/// </summary>
public sealed class FlatFileGeneratedKeyTests
{
    [Fact]
    public void FlatFile_PrefetchesTheIdFromTheTableSequence()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.FlatFile,
            new fakeDbFactory(SupportedDatabase.FlatFile), NullLogger<SqlDialect>.Instance);

        Assert.Equal(GeneratedKeyPlan.PrefetchSequence, dialect.GetGeneratedKeyPlan());
        Assert.Equal("VALUES (NEXT VALUE FOR \"items_seq\")", dialect.GetSequenceNextValQuery("items_seq"));
    }

    [Fact]
    public async Task CreateAsync_OnFlatFile_PopulatesTheIdAndSendsIt()
    {
        var factory = new fakeDbFactory(SupportedDatabase.FlatFile);
        await using var context = new DatabaseContext("path=/data;EmulatedProduct=FlatFile", factory);
        var gateway = new TableGateway<Item, long>(context);
        factory.EnqueueReaderResult(new[] { new Dictionary<string, object> { ["value"] = 42L } });

        var item = new Item { Name = "widget" };
        Assert.True(await gateway.CreateAsync(item));

        Assert.Equal(42L, item.Id);
        var insert = factory.CreatedConnections.SelectMany(c => c.ExecutedNonQueryCommands)
            .Single(c => c.CommandText.Contains("INSERT INTO"));
        Assert.Contains("\"id\"", insert.CommandText[..insert.CommandText.IndexOf(')')]);
        Assert.Contains(insert.Parameters, p => Equals(p.Value, 42L));
    }

    // Found live 2026-09-29: the prefetch ran the sequence query as ExecutionType.Read, so it got a
    // read-only connection and pengdows.flatfile rejected it ("NEXT VALUE FOR requires a writable
    // connection or transaction"). Advancing a sequence is a write. InterBase shares the code path but
    // has no read-only connection parameter, so only FlatFile can observe the split.
    [Theory]
    [InlineData(SupportedDatabase.FlatFile, "path=/data;EmulatedProduct=FlatFile", false)]
    [InlineData(SupportedDatabase.FlatFile, "path=/data;EmulatedProduct=FlatFile", true)]
    public async Task CreateAsync_PrefetchesTheSequenceOnAWriteConnection(SupportedDatabase database,
        string connectionString, bool withCancellationToken)
    {
        var factory = new fakeDbFactory(database);
        await using var context = new DatabaseContext(connectionString, factory);
        Assert.Equal(GeneratedKeyPlan.PrefetchSequence, context.Dialect.GetGeneratedKeyPlan());
        var readOnlyParameter = ((SqlDialect)context.Dialect).GetReadOnlyConnectionParameter();
        Assert.False(string.IsNullOrEmpty(readOnlyParameter));
        var gateway = new TableGateway<Item, long>(context);
        factory.EnqueueReaderResult(new[] { new Dictionary<string, object> { ["value"] = 42L } });

        var item = new Item { Name = "widget" };
        Assert.True(withCancellationToken
            ? await gateway.CreateAsync(item, context, System.Threading.CancellationToken.None)
            : await gateway.CreateAsync(item));

        var sequenceConnections = factory.CreatedConnections.Where(c =>
            c.ExecutedReaderTexts.Concat(c.ExecutedScalarTexts).Any(t => t.Contains("_seq", System.StringComparison.Ordinal)))
            .ToList();
        Assert.NotEmpty(sequenceConnections);
        Assert.All(sequenceConnections, c => Assert.DoesNotContain(readOnlyParameter!, c.ConnectionString,
            System.StringComparison.OrdinalIgnoreCase));
    }

    [Table("items")]
    private sealed class Item
    {
        [Id(false)] [Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }
}
