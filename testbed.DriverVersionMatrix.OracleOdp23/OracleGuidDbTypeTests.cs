using System.Data;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace testbed.DriverVersionMatrix.OracleOdp23;

// FEAT-008: OracleDialect.cs (pengdows.crud/dialects/OracleDialect.cs) used to document a
// one-line claim used to justify RemapDbType: "Oracle ODP.NET 23.x throws ArgumentException for
// DbType.Boolean and DbType.Guid." Investigation found that claim conflated two genuinely
// different, independent facts:
//
// 1. DbType.Guid: rejected by OracleParameter.DbType's setter itself (ArgumentException,
//    "Value does not fall within the expected range") -- immediate, client-side only, no
//    connection needed. Confirmed identical across the full tested ODP.NET range, 3.21.230
//    (oldest 21c-line release) through 23.26.300 (newest 23.x release as of this check) --
//    see sibling project OracleOdp321. Not actually "23.x"-specific at all.
// 2. DbType.Boolean: OracleParameter.DbType accepts the assignment fine in every version tested
//    (no exception here, contrary to the original comment). The real failure is at bind+execute
//    time, and depends on the *server* version, not the driver: Oracle Database 23c/23ai added a
//    genuine native BOOLEAN type; binding DbType.Boolean succeeds against a 23c+ server but
//    throws InvalidCastException ("Specified cast is not valid") against an 18c/21c server that
//    predates native boolean support -- confirmed live against both
//    gvenzl/oracle-free:23.26.2-slim-faststart and gvenzl/oracle-xe:21-slim-faststart, the exact
//    images testbed's own Oracle server-version matrix already uses
//    (testbed/ParallelTestOrchestrator.cs). This is a server-version compatibility fact, not a
//    driver-version one, so its regression coverage lives in testbed's regular Oracle test
//    provider (which already runs against 18c/21c/23.26.2) rather than here.
//
// This project only covers fact 1 -- the genuine driver-version question FEAT-008 is about.
public class OracleGuidDbTypeTests
{
    [Fact]
    public void SettingDbTypeGuid_ThrowsArgumentException()
    {
        var parameter = new OracleParameter();

        // No connection needed -- this is a client-side setter validation. If this test starts
        // passing (no exception), OracleDialect.cs's RemapDbType no longer needs to remap
        // DbType.Guid -> DbType.String for this driver version.
        var ex = Assert.Throws<ArgumentException>(() => parameter.DbType = DbType.Guid);
        Assert.Contains("expected range", ex.Message);
    }

    [Fact]
    public void SettingDbTypeBoolean_DoesNotThrowOnAssignment()
    {
        var parameter = new OracleParameter();

        // Contrary to the original comment: this never throws at assignment time in any tested
        // driver version. The real Boolean failure mode (InvalidCastException, server-version
        // dependent) only appears at bind+execute time against a pre-23c server -- see this
        // file's header comment and testbed's Oracle test provider for that coverage.
        parameter.DbType = DbType.Boolean;
        Assert.Equal(DbType.Boolean, parameter.DbType);
    }
}
