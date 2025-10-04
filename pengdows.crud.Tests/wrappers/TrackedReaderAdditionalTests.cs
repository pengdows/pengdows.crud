using System;
using System.Collections.Generic;
using Moq;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.wrappers;

public class TrackedReaderAdditionalTests
{
    [Fact]
    public void DelegatedMethods_ForwardToUnderlyingReader()
    {
        var row = new Dictionary<string, object>
        {
            ["ByteField"] = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            ["CharField"] = "teststring",
            ["IntField"] = 42
        };
        
        using var reader = new fakeDbDataReader(new[] { row });
        reader.Read();
        
        var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false);

        var bytes = new byte[4];
        var chars = new char[3];
        
        // Test basic delegation methods
        Assert.True(tracked.GetBytes(0, 2, bytes, 0, 4) >= 0);
        Assert.True(tracked.GetChars(1, 2, chars, 0, 3) >= 0);
        Assert.NotNull(tracked.GetDataTypeName(0));
        Assert.True(tracked.GetValues(new object[3]) > 0);
    }

    [Fact]
    public void Close_CallsUnderlyingReader()
    {
        var row = new Dictionary<string, object> { ["Field"] = "value" };
        using var reader = new fakeDbDataReader(new[] { row });
        
        var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false);

        // Test that Close doesn't throw
        tracked.Close();
        
        // After close, reader should be closed
        Assert.True(reader.IsClosed);
    }

    [Fact]
    public void GetData_ForwardsToUnderlyingReader()
    {
        var row = new Dictionary<string, object> { ["Field"] = "value" };
        using var reader = new fakeDbDataReader(new[] { row });
        
        var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false);

        // Test that GetData forwards to the underlying reader without throwing
        // Most databases don't actually use GetData for nested readers
        var result = tracked.GetData(0);
        
        // Just verify the method doesn't throw and returns some result
        Assert.NotNull(result);
    }
}
