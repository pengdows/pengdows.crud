using testbed;

namespace pengdows.crud.IntegrationTests.Infrastructure;

// The all-types integration matrix runs on one image per database: TestbedImageMatrix.Latest, the first (newest)
// entry of that database's list. The list feeds the testbed's per-version checks; Latest is not affected by the
// TESTBED_<PROVIDER>_IMAGES override, which picks images for a testbed run, not the version the matrix runs on.
public class TestbedImageMatrixLatestTests
{
    [Theory]
    [InlineData("PostgreSQL")]
    [InlineData("MySQL")]
    [InlineData("MariaDB")]
    [InlineData("SQL Server")]
    [InlineData("CockroachDB")]
    [InlineData("Firebird")]
    [InlineData("TiDB")]
    [InlineData("YugabyteDB")]
    [InlineData("Oracle")]
    [InlineData("Db2")]
    public void Latest_IsTheFirstEntryOfTheList(string provider)
    {
        var first = TestbedImageMatrix.DefaultsFor(provider)[0].Image;

        Assert.Equal(first, TestbedImageMatrix.Latest(provider));
    }

    [Fact]
    public void Latest_IgnoresTheImageOverride()
    {
        const string variable = "TESTBED_POSTGRESQL_IMAGES";
        var original = Environment.GetEnvironmentVariable(variable);
        try
        {
            var expected = TestbedImageMatrix.DefaultsFor("PostgreSQL")[0].Image;
            Environment.SetEnvironmentVariable(variable, "postgres:9.5");

            Assert.Equal(expected, TestbedImageMatrix.Latest("PostgreSQL"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }

    [Fact]
    public void Latest_ForAnUnlistedProvider_Throws()
    {
        Assert.Throws<ArgumentException>(() => TestbedImageMatrix.Latest("NoSuchDatabase"));
    }

    // The newest published release of each line: MySQL 9.x is where VECTOR exists (9.0+), MariaDB 11.7+ has VECTOR, and
    // PostgreSQL 16 takes its last minor. The all-types matrix runs on these.
    [Theory]
    [InlineData("MySQL", "mysql:9.7")]
    [InlineData("MariaDB", "mariadb:11.8")]
    [InlineData("PostgreSQL", "postgres:16.15")]
    public void Latest_IsTheNewestReleaseOfItsLine(string provider, string image)
    {
        Assert.Equal(image, TestbedImageMatrix.Latest(provider));
    }
}
