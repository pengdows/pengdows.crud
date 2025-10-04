using System;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class fakeDbConnectionTests
{
    [Fact]
    public void GetSchema_UnknownProduct_ReturnsDefaultSchema()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Unknown}";
        conn.Open();
        var schema = conn.GetSchema();
        Assert.True(schema.Rows[0].Field<bool>("SupportsNamedParameters"));
        Assert.NotEqual(false, schema.Rows[0].Field<bool>("SupportsNamedParameters"));
    }

    [Fact]
    public void GetSchema_EmulatedProductNotConfigured_Throws()
    {
        var conn = new fakeDbConnection();
        Assert.Throws<InvalidOperationException>(() => conn.GetSchema());
    }
}
