using System.Data;
using System.Data.Common;
using Moq;
using pengdow.crud;
using pengdow.crud.FakeDb;
using Xunit;

namespace pengdow.crud.Tests;

public class SqlContainerMakeParameterNameTests
{
    [Fact]
    public void MakeParameterName_DelegatesToContext()
    {
        var context = new Mock<IDatabaseContext>();
        context.Setup(c => c.MakeParameterName(It.IsAny<DbParameter>())).Returns("@p0");
        var container = new SqlContainer(context.Object);
        var param = new FakeDbParameter { DbType = DbType.Int32, ParameterName = "p" };

        var result = container.MakeParameterName(param);

        Assert.Equal("@p0", result);
        context.Verify(c => c.MakeParameterName(param), Times.Once);
    }
}
