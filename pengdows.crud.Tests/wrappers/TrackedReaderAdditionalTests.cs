using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.wrappers;

public class TrackedReaderAdditionalTests
{
    [Fact]
    public void DelegatedMethods_ForwardToUnderlyingReader()
    {
        var reader = new Mock<DbDataReader>();
        var bytes = new byte[4];
        var chars = new char[3];
        var dataReader = new Mock<IDataReader>().Object;
        reader.Setup(r => r.GetBytes(1, 2, bytes, 3, 4)).Returns(7);
        reader.Setup(r => r.GetChars(1, 2, chars, 3, 4)).Returns(8);
        reader.Setup(r => r.GetData(1)).Returns(dataReader);
        reader.Setup(r => r.GetDataTypeName(1)).Returns("varchar");
        reader.Setup(r => r.GetValues(It.IsAny<object[]>())).Returns(5);

        var tracked = new TrackedReader(reader.Object, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false);

        Assert.Equal(7, tracked.GetBytes(1, 2, bytes, 3, 4));
        Assert.Equal(8, tracked.GetChars(1, 2, chars, 3, 4));
        Assert.Same(dataReader, tracked.GetData(1));
        Assert.Equal("varchar", tracked.GetDataTypeName(1));
        Assert.Equal(5, tracked.GetValues(new object[2]));
    }

    [Fact]
    public void Close_CallsUnderlyingReader()
    {
        var reader = new Mock<DbDataReader>();
        var tracked = new TrackedReader(reader.Object, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false);

        tracked.Close();

        reader.Verify(r => r.Close(), Times.Once);
    }
}
