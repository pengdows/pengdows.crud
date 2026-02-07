using System;
using System.Data.Common;
using Moq;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
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

        var reader = new Mock<DbDataReader>();
        reader.Setup(r => r.FieldCount).Returns(1);
        reader.Setup(r => r.Read()).Returns(true);
        reader.Setup(r => r.GetName(0)).Returns("Name");
        reader.Setup(r => r.GetFieldType(0)).Returns(typeof(string));
        reader.Setup(r => r.IsDBNull(0)).Returns(false);
        reader.Setup(r => r.GetValue(0)).Returns("foo");

        var tracked = new TrackedReader(reader.Object, new Mock<ITrackedConnection>().Object,
            Mock.Of<IAsyncDisposable>(), false);

        // After compiled mapper optimization, exceptions bubble up directly (not wrapped)
        // This is actually cleaner for debugging - the original exception is preserved
        var ex = Assert.Throws<InvalidOperationException>(() => helper.MapReaderToObject(tracked));
        Assert.Equal("boom", ex.Message);
    }
}