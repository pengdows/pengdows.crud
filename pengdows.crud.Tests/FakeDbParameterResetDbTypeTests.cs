using System.Data;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DbParameter.ResetDbType "resets the DbType property to its original settings": a real provider's
/// parameter goes back to the type it infers from its value, as a new one does. fakeDb set DbType.Object
/// instead, so it could not show a dialect leaving a parameter's type for the provider to infer.
/// </summary>
public sealed class FakeDbParameterResetDbTypeTests
{
    [Fact]
    public void ResetDbType_RestoresTheTypeOfANewParameter()
    {
        var parameter = new fakeDbParameter { DbType = DbType.Int32 };

        parameter.ResetDbType();

        Assert.Equal(new fakeDbParameter().DbType, parameter.DbType);
    }
}
