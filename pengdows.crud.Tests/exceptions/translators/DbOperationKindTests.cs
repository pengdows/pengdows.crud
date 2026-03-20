using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class DbOperationKindTests
{
    [Fact]
    public void DbOperationKind_HasExpectedValues()
    {
        Assert.Equal(0, (int)DbOperationKind.Unknown);
        Assert.Equal(1, (int)DbOperationKind.Query);
        Assert.Equal(2, (int)DbOperationKind.Insert);
        Assert.Equal(3, (int)DbOperationKind.Update);
        Assert.Equal(4, (int)DbOperationKind.Delete);
        Assert.Equal(5, (int)DbOperationKind.Commit);
        Assert.Equal(6, (int)DbOperationKind.Rollback);
    }
}
