using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using pengdows.crud.types.converters;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-017: gateway hydration must coerce with the gateway's provider, as SqlContainer's scalar
/// reads and parameter writes already do; the compiled mapper passed no options, so a converter
/// keyed on the provider saw <see cref="SupportedDatabase.Unknown"/> for an entity property.
/// (3.0 has no provider-scoped coercion registration, so the provider is observed through a
/// registered converter, which receives it on every read.)
/// </summary>
public sealed class GatewayProviderScopedCoercionTests
{
    static GatewayProviderScopedCoercionTests()
    {
        // The marker type is private to this test class, so the shared registry entry can't affect
        // any other test.
        CoercionRegistry.Shared.RegisterConverter(new ProviderEchoConverter());
    }

    [Theory]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.PostgreSql)]
    public async Task RetrieveOneAsync_ConvertsWithTheGatewaysProvider(SupportedDatabase database)
    {
        var (context, exec) = Context(database);
        await using var _ = context;
        exec.EnqueueReaderResult(MarkerReader());
        var gateway = new TableGateway<MarkerRow, int>(context);

        var row = await gateway.RetrieveOneAsync(1);

        Assert.Equal($"{database}:stored", row!.Marker!.Text);
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

    private sealed class ProviderEchoConverter : AdvancedTypeConverter<Marker>
    {
        public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out Marker result)
        {
            result = new Marker { Text = $"{provider}:{value}" };
            return value is string;
        }
    }

    [Table("t")]
    private sealed class MarkerRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("marker", DbType.String)] public Marker? Marker { get; set; }
    }
}
