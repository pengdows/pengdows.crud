using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class fakeDbConnectionTests
{
    // A found-during-audit inconsistency: Close() incremented CloseCount even when the
    // connection was already closed, so a redundant Close() call (e.g. from Dispose(bool)
    // unconditionally calling Close()) inflated a test's CloseCount assertion beyond the number
    // of real caller-initiated closes. Real DbConnection.Close() is a no-op on an
    // already-closed connection; this locks in the same idempotency for the fake.
    [Fact]
    public void Close_CalledTwice_OnlyIncrementsCloseCountOnce()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.Open();

        conn.Close();
        conn.Close();

        Assert.Equal(1, conn.CloseCount);
    }

    [Fact]
    public void Close_AlreadyClosed_DoesNotRaiseStateChangedEvent()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.Open();
        conn.Close();

        var raised = false;
        conn.StateChange += (_, _) => raised = true;

        conn.Close();

        Assert.False(raised);
    }

    // A found-during-audit asymmetry: Dispose(bool) already swallows a configured close
    // failure (the .NET convention that Dispose must not throw), but DisposeAsync did not —
    // it let the close exception propagate uncaught. This locks in the same swallow behavior
    // for the async path.
    [Fact]
    public async Task DisposeAsync_CloseConfiguredToFail_DoesNotThrow()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        conn.Open();
        conn.SetFailOnClose(new InvalidOperationException("boom"));

        await conn.DisposeAsync();
    }

    // fakeDb is meant to be a drop-in ADO.NET provider substitute, not just something
    // pengdows.crud's own internals happen to work against. Database/DataSource/ConnectionTimeout
    // are standard DbConnection properties application code can legitimately read directly (via
    // ITrackedConnection, which passes them straight through) -- found during a fakeDb audit that
    // Database returned the *emulated product name* (e.g. "PostgreSql") instead of an actual
    // parsed database/catalog name, DataSource always returned the literal "FakeSource" ignoring
    // the connection string entirely, and ConnectionTimeout was hardcoded to 0 regardless of any
    // "Connect Timeout=" the connection string specified. None of these are read anywhere inside
    // pengdows.crud's own logic (checked before fixing), so this didn't break this repo's own test
    // suite -- but any application code reading these properties directly would get misleading
    // values from fakeDb versus what the same connection string would produce against a real
    // provider.
    [Fact]
    public void ConnectionTimeout_NotSpecified_DefaultsToFifteen()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";

        Assert.Equal(15, conn.ConnectionTimeout);
    }

    [Fact]
    public void ConnectionTimeout_SpecifiedInConnectionString_ReturnsParsedValue()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite};Connect Timeout=45";

        Assert.Equal(45, conn.ConnectionTimeout);
    }

    [Fact]
    public void Database_NotSpecified_ReturnsEmptyString()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";

        Assert.Equal(string.Empty, conn.Database);
    }

    [Fact]
    public void Database_SpecifiedInConnectionString_ReturnsIt()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite};Database=MyAppDb";

        Assert.Equal("MyAppDb", conn.Database);
    }

    [Fact]
    public void DataSource_NotSpecified_KeepsDefaultPlaceholder()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"EmulatedProduct={SupportedDatabase.Sqlite}";

        Assert.Equal("FakeSource", conn.DataSource);
    }

    [Fact]
    public void DataSource_SpecifiedInConnectionString_ReturnsIt()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=myserver.example.com;EmulatedProduct={SupportedDatabase.Sqlite}";

        Assert.Equal("myserver.example.com", conn.DataSource);
    }

    [Fact]
    public void GetSchema_UnknownProduct_ReturnsDefaultSchema()
    {
        var conn = new fakeDbConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Unknown}";
        conn.Open();
        var schema = conn.GetSchema();
        Assert.True(schema.Rows[0].Field<bool>("SupportsNamedParameters"));
        Assert.True(schema.Rows[0].Field<bool>("SupportsNamedParameters"));
    }

    [Fact]
    public void GetSchema_EmulatedProductNotConfigured_Throws()
    {
        var conn = new fakeDbConnection();
        Assert.Throws<InvalidOperationException>(() => conn.GetSchema());
    }
}