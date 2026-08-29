using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using System.Threading.Tasks;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class DatabaseDetectionDiagnosticsTests
{
    [Fact]
    public void DetectFromConnectionWithDetail_ExposesResolvedProductAndProbeEvidence()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        using var connection = (fakeDbConnection)factory.CreateConnection();
        connection.SetFailOnCommand(true);

        var result = DatabaseDetection.DetectFromConnectionWithDetail(connection);

        Assert.Equal(SupportedDatabase.MySql, result.ResolvedProduct);
        Assert.Contains(result.Attempts, attempt => !attempt.Succeeded);
    }

    [Fact]
    public async Task DetectFromConnectionWithDetailAsync_ExposesResolvedProductAndProbeEvidence()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        await using var connection = factory.CreateConnection();

        var result = await DatabaseDetection.DetectFromConnectionWithDetailAsync(connection);

        Assert.Equal(SupportedDatabase.PostgreSql, result.ResolvedProduct);
        Assert.Contains(result.Attempts, attempt => attempt.Succeeded);
    }
}
