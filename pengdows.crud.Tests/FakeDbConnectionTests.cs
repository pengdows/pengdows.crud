using System;
using System.Data;
using pengdows.crud.FakeDb;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class FakeDbConnectionTests
{
    [Fact]
    public void GetSchema_UnknownProduct_ReturnsDefaultSchema()
    {
        var conn = new FakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Unknown}";
        conn.Open();
        var schema = conn.GetSchema();
        Assert.True(schema.Rows[0].Field<bool>("SupportsNamedParameters"));
        Assert.NotEqual(false, schema.Rows[0].Field<bool>("SupportsNamedParameters"));
    }

    [Fact]
    public void GetSchema_EmulatedProductNotConfigured_Throws()
    {
        var conn = new FakeDbConnection();
        Assert.Throws<InvalidOperationException>(() => conn.GetSchema());
    }
}
