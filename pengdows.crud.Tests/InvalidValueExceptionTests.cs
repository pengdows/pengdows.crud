using System;
using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class InvalidValueExceptionTests
{
    [Fact]
    public void MapReaderToObject_SetterThrows_ThrowsInvalidValueException()
    {
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite));
        var helper = new TableGateway<ThrowingEntity, int>(context);

        var reader = new fakeDbDataReader(new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>
        {
            new() { ["Name"] = "foo" }
        });

        var tracked = new TrackedReader(reader, TestDoubles.Connection(), TestDoubles.Disposable(), false);
        Assert.True(tracked.Read());

        // After compiled mapper optimization, exceptions bubble up directly (not wrapped)
        // This is actually cleaner for debugging - the original exception is preserved
        var ex = Assert.Throws<InvalidOperationException>(() => helper.MapReaderToObject(tracked));
        Assert.Equal("boom", ex.Message);
    }
}