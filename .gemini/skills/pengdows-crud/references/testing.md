# Testing with `pengdows.crud`

`pengdows.crud` is designed for high testability.

## Unit Testing with `fakeDb`

`pengdows.crud.fakeDb` provides a mock ADO.NET provider for fast, isolated unit tests.

- **What it does:** Implements full `DbConnection`, `DbCommand`, `DbDataReader` APIs; allows SQL generation and parameter verification.
- **What it doesn't do:** Enforce SQL constraints, triggers, or data types.

```csharp
[Fact]
public async Task SearchByName_GeneratesCorrectSQL()
{
    // 1. Arrange - Use fakeDb
    var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
    var context = new DatabaseContext("Data Source=:memory:", factory);
    var gateway = new CustomerGateway(context);

    // 2. Act
    var results = await gateway.SearchByNameAsync("Acme");

    // 3. Assert - Verify SQL generation without a DB
    Assert.Contains("WHERE c.name LIKE @p0", gateway.LastGeneratedSql);
}
```

## Integration Testing

Use **Testcontainers** to validate behavior against real database engines.

```csharp
public class CustomerIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres;
    private DatabaseContext _context;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder().Build();
        await _postgres.StartAsync();

        _context = new DatabaseContext(
            _postgres.GetConnectionString(),
            NpgsqlFactory.Instance
        );
    }

    [Fact]
    public async Task CreateAndRetrieve_Roundtrips()
    {
        var gateway = new TableGateway<Customer, int>(_context);
        var customer = new Customer { Name = "Test" };

        await gateway.CreateAsync(customer);
        var retrieved = await gateway.RetrieveOneAsync(customer.Id);

        Assert.Equal("Test", retrieved.Name);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}
```

## Testing Standards

- **TDD (Mandatory):** Write failing test first, then implement.
- **High Coverage:** 83% minimum (95% for new features).
- **Fast Execution:** Unit tests should complete in seconds.
