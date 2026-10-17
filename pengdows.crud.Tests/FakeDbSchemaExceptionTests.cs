using System;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A real provider may not implement GetSchema at all, or may refuse one collection (Microsoft.Data.Sqlite: "The requested
/// collection is not defined"). fakeDb can model either: SchemaException makes every GetSchema overload throw it.
/// </summary>
public class FakeDbSchemaExceptionTests
{
    private static fakeDbConnection Connection() => new()
    {
        EmulatedProduct = SupportedDatabase.Sqlite,
        ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite"
    };

    [Fact]
    public void SchemaException_MakesEveryGetSchemaOverloadThrowIt()
    {
        var failure = new ArgumentException("The requested collection is not defined");
        var connection = Connection();
        connection.SchemaException = failure;

        Assert.Same(failure, Assert.Throws<ArgumentException>(() => connection.GetSchema()));
        Assert.Same(failure, Assert.Throws<ArgumentException>(() => connection.GetSchema("DataSourceInformation")));
        Assert.Same(failure, Assert.Throws<ArgumentException>(() => connection.GetSchema("Tables", new[] { "x" })));
    }

    [Fact]
    public void WithoutASchemaException_GetSchemaStillReturnsTheEmbeddedSchema()
    {
        var connection = Connection();

        Assert.NotEmpty(connection.GetSchema("DataSourceInformation").Rows);
    }

    [Fact]
    public void ClearingTheSchemaException_RestoresGetSchema()
    {
        var connection = Connection();
        connection.SchemaException = new NotSupportedException();
        connection.SchemaException = null;

        Assert.NotEmpty(connection.GetSchema("DataSourceInformation").Rows);
    }
}
