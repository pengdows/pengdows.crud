using System.Data;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace testbed.DriverVersionMatrix.OracleOdp321;

// FEAT-008: sibling of testbed.DriverVersionMatrix.OracleOdp23.OracleGuidDbTypeTests, pinned at
// Oracle.ManagedDataAccess.Core 3.21.230 (the oldest tested release in the "21c" driver line --
// Oracle packages it under a "3.21.x" version number, not "21.x"). See the sibling project's
// header comment for the full investigation: OracleDialect.cs's original comment claimed this
// was an "ODP.NET 23.x" behavior; this test proves the DbType.Guid rejection is actually
// identical at the oldest tested driver version too, so it was never 23.x-specific.
public class OracleGuidDbTypeTests
{
    [Fact]
    public void SettingDbTypeGuid_ThrowsArgumentException()
    {
        var parameter = new OracleParameter();

        var ex = Assert.Throws<ArgumentException>(() => parameter.DbType = DbType.Guid);
        Assert.Contains("expected range", ex.Message);
    }
}
