#region

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#endregion

namespace pengdow.crud.Tests;

public class IDataSourceInformationTests
{
    [Fact]
    public void IDataSourceInformation_ImplementsRequiredProperties()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        var info = new DataSourceInformation(conn, NullLoggerFactory.Instance);
        Assert.False(string.IsNullOrWhiteSpace(info.CompositeIdentifierSeparator));
        Assert.False(string.IsNullOrWhiteSpace(info.ParameterMarker));
    }
}