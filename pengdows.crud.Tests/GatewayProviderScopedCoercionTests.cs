using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-017: gateway hydration must coerce with the gateway's provider, as SqlContainer's scalar
/// reads and parameter writes already do; the compiled mapper passed no options, so a
/// provider-scoped <see cref="CoercionRegistry"/> entry never applied to an entity property.
/// </summary>
public sealed class GatewayProviderScopedCoercionTests
{
    static GatewayProviderScopedCoercionTests()
    {
        // Registered for Firebird only; the marker type is private to this test class, so the
        // shared registry entry can't affect any other test.
        CoercionRegistry.Shared.Register(SupportedDatabase.Firebird, new FirebirdMarkerCoercion());
    }

    [Fact]
    public async Task RetrieveOneAsync_ProviderScopedCoercion_AppliesOnItsProvider()
    {
        var (context, exec) = Context(SupportedDatabase.Firebird);
        await using var _ = context;
        exec.EnqueueReaderResult(MarkerReader());
        var gateway = new TableGateway<MarkerRow, int>(context);

        var row = await gateway.RetrieveOneAsync(1);

        Assert.Equal("firebird:stored", row!.Marker!.Text);
    }

    [Fact]
    public async Task RetrieveOneAsync_ProviderScopedCoercion_DoesNotApplyOnAnotherProvider()
    {
        var (context, exec) = Context(SupportedDatabase.PostgreSql);
        await using var _ = context;
        exec.EnqueueReaderResult(MarkerReader());
        var gateway = new TableGateway<MarkerRow, int>(context);

        await Assert.ThrowsAsync<DataMappingException>(async () => await gateway.RetrieveOneAsync(1));
    }

    private static fakeDbDataReader MarkerReader() =>
        new(new[] { new Dictionary<string, object> { ["id"] = 1, ["marker"] = "stored" } });

    private static (DatabaseContext Context, fakeDbConnection Exec) Context(SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = database });
        var exec = new fakeDbConnection { EmulatedProduct = database };
        factory.Connections.Add(exec);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Server=x;Database=y;EmulatedProduct={database}",
            DbMode = DbMode.Standard
        }, factory);
        return (context, exec);
    }

    private sealed class Marker
    {
        public string Text { get; init; } = "";
    }

    private sealed class FirebirdMarkerCoercion : DbCoercion<Marker>
    {
        public override bool TryRead(in DbValue src, out Marker? value)
        {
            value = src.RawValue is string text ? new Marker { Text = "firebird:" + text } : null;
            return value != null;
        }
    }

    [Table("t")]
    private sealed class MarkerRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("marker", DbType.String)] public Marker? Marker { get; set; }
    }
}
