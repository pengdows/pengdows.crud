#region

using Microsoft.Data.Sqlite;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class IDataSourceInformationTests
{
    [Fact]
    public void IDataSourceInformation_ImplementsRequiredProperties()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        var info = new DataSourceInformation(conn);
        Assert.False(string.IsNullOrWhiteSpace(info.CompositeIdentifierSeparator));
        Assert.False(string.IsNullOrWhiteSpace(info.ParameterMarker));
    }
}