using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DEC-007 (maintainer decision 2026-09-30: throw): where the dialect's only way to read back a
/// database-generated id is a [CorrelationToken] column (GeneratedKeyPlan.CorrelationToken:
/// Snowflake), CreateAsync on an entity without one returned true and left the [Id(false)] at 0. It
/// now throws NotSupportedException naming the fix, before anything is written.
/// </summary>
public sealed class CorrelationTokenRequiredTests
{
    private static DatabaseContext Snowflake(fakeDbFactory factory) =>
        new("Account=a;User=u;Password=p;Db=d;EmulatedProduct=Snowflake", factory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAsync_GeneratedIdWithoutCorrelationToken_ThrowsBeforeWriting(bool withToken)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        await using var context = Snowflake(factory);
        Assert.Equal(GeneratedKeyPlan.CorrelationToken, context.Dialect.GetGeneratedKeyPlan());
        var gateway = new TableGateway<GeneratedIdOnly, long>(context);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            if (withToken)
            {
                await gateway.CreateAsync(new GeneratedIdOnly { Name = "w" }, context, CancellationToken.None);
            }
            else
            {
                await gateway.CreateAsync(new GeneratedIdOnly { Name = "w" });
            }
        });

        Assert.Contains("[CorrelationToken]", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.CreatedConnections,
            c => c.ExecutedNonQueryTexts.Any(t => t.Contains("INSERT", StringComparison.OrdinalIgnoreCase)));
    }

    // Only a dialect that positively knows it has no other mechanism refuses. The generic Unknown
    // dialect keeps 2.0.5's behavior (insert succeeds, id left unset): throwing there would break
    // working code on databases the library doesn't recognize.
    [Fact]
    public async Task CreateAsync_UnknownDatabase_KeepsInsertingWithoutTheId()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Unknown);
        await using var context = new DatabaseContext("Data Source=x;EmulatedProduct=Unknown", factory);
        var gateway = new TableGateway<GeneratedIdOnly, long>(context);

        Assert.True(await gateway.CreateAsync(new GeneratedIdOnly { Name = "w" }));
    }

    [Fact]
    public async Task CreateAsync_ClientProvidedId_IsUnaffected()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        await using var context = Snowflake(factory);
        var gateway = new TableGateway<ClientId, long>(context);

        Assert.True(await gateway.CreateAsync(new ClientId { Id = 7, Name = "w" }));
    }

    [Table("generated_only")]
    private sealed class GeneratedIdOnly
    {
        [Id(false)] [Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    [Table("client_id")]
    private sealed class ClientId
    {
        [Id] [Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }
}
