using System;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Moq;
using pengdows.crud;
using pengdows.crud.enums;
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
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", new fakeDbFactory(SupportedDatabase.Sqlite));
        var helper = new EntityHelper<ThrowingEntity, int>(context);

        var reader = new Mock<DbDataReader>();
        reader.Setup(r => r.FieldCount).Returns(1);
        reader.Setup(r => r.Read()).Returns(true);
        reader.Setup(r => r.GetName(0)).Returns("Name");
        reader.Setup(r => r.GetFieldType(0)).Returns(typeof(string));
        reader.Setup(r => r.IsDBNull(0)).Returns(false);
        reader.Setup(r => r.GetValue(0)).Returns("foo");

        var tracked = new TrackedReader(reader.Object, new Mock<ITrackedConnection>().Object, Mock.Of<IAsyncDisposable>(), false);

        Assert.Throws<InvalidValueException>(() => helper.MapReaderToObject(tracked));
    }
}
