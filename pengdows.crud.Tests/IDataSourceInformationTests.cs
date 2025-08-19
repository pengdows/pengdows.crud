#region

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class IDataSourceInformationTests
{
    [Fact]
    public void IDataSourceInformation_ImplementsRequiredProperties()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        using var tracked = new TrackedConnection(conn);
        var info = DataSourceInformation.Create(tracked, SqliteFactory.Instance, NullLoggerFactory.Instance);
        Assert.False(string.IsNullOrWhiteSpace(info.CompositeIdentifierSeparator));
        Assert.False(string.IsNullOrWhiteSpace(info.ParameterMarker));
    }
}