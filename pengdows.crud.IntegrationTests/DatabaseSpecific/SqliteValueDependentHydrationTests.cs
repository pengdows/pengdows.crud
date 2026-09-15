using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// SQLite's affinity rules permit one DECIMAL column to be returned as an integer, real, or text
/// value depending on the value stored. Hydration must target the declared CLR property type, not
/// the provider's per-row runtime CLR type.
/// </summary>
public sealed class SqliteValueDependentHydrationTests : SqliteTestBase
{
    public SqliteValueDependentHydrationTests(ITestOutputHelper output)
        : base(output)
    {
    }

    protected override async Task SetupDatabaseAsync()
    {
        await ExecuteSqlAsync("CREATE TABLE value_dependent_decimal (id INTEGER PRIMARY KEY, amount DECIMAL NOT NULL)");
        await ExecuteSqlAsync("INSERT INTO value_dependent_decimal (id, amount) VALUES (1, 1), (2, 1.5), (3, '2.75')");
    }

    [Fact]
    public async Task DecimalAffinityValues_HydrateToDeclaredDecimalType()
    {
        var gateway = new TableGateway<SqliteValueDependentDecimal, long>(Context);

        var integerProviderType = await GetProviderFieldTypeAsync(1);
        var realProviderType = await GetProviderFieldTypeAsync(2);

        Assert.Equal(typeof(long), integerProviderType);
        Assert.Equal(typeof(double), realProviderType);
        Assert.NotEqual(integerProviderType, realProviderType);

        var integerStorage = await gateway.RetrieveOneAsync(1L);
        var realStorage = await gateway.RetrieveOneAsync(2L);
        var textStorage = await gateway.RetrieveOneAsync(3L);

        Assert.NotNull(integerStorage);
        Assert.NotNull(realStorage);
        Assert.NotNull(textStorage);
        Assert.Equal(1m, integerStorage.Amount);
        Assert.Equal(1.5m, realStorage.Amount);
        Assert.Equal(2.75m, textStorage.Amount);
    }

    private async Task<Type> GetProviderFieldTypeAsync(long id)
    {
        await using var container = Context.CreateSqlContainer(
            "SELECT amount FROM value_dependent_decimal WHERE id = @id");
        container.AddParameterWithValue("id", DbType.Int64, id);
        await using var reader = await container.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return reader.GetFieldType(0);
    }
}

[Table("value_dependent_decimal")]
public sealed class SqliteValueDependentDecimal
{
    [Id]
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [Column("amount", DbType.Decimal)]
    public decimal Amount { get; set; }
}
