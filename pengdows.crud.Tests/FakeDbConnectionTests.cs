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