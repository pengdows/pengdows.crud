#region

using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class IDataSourceInformationTests
{
    [Fact]
    public void IDataSourceInformation_ImplementsRequiredProperties()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var conn = factory.CreateConnection();
        conn.ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite";
        using var tracked = new TrackedConnection(conn);
        var info = DataSourceInformation.Create(tracked, factory, NullLoggerFactory.Instance);
        Assert.False(string.IsNullOrWhiteSpace(info.CompositeIdentifierSeparator));
        Assert.False(string.IsNullOrWhiteSpace(info.ParameterMarker));
        Assert.True(info.MaxOutputParameters >= 0);
    }
}