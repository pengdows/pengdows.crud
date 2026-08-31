using MySqlConnector;
using Xunit;

namespace testbed.DriverVersionMatrix.MySqlConnector200;

// FEAT-008: MySqlDialect.cs (pengdows.crud/dialects/MySqlDialect.cs) documents a claim used to
// pick GetGeneratedKeyPlan()'s strategy: "MySqlConnector 2.x deliberately does not support
// AllowMultipleStatements... it is not a known connection string property." The dialect
// deliberately never injects "Allow Multiple Statements" into a MySqlConnector connection
// string because of this — instead using GeneratedKeyPlan.ReaderInsertedId (reads LastInsertedId
// from the MySqlDataReader OK packet, no multi-statement support required).
//
// This verifies the claim directly against MySqlConnectionStringBuilder rather than trusting the
// comment: does setting this key actually throw, and is the behavior consistent across the 2.x
// line? Pinned at 2.0.0, the oldest stable 2.x release; sibling project
// MySqlConnector262 pins 2.6.2, the newest stable 2.x release as of this check.
public class AllowMultipleStatementsUnsupportedTests
{
    [Fact]
    public void SettingAllowMultipleStatements_ThrowsArgumentException()
    {
        var builder = new MySqlConnectionStringBuilder();

        // Confirms the dialect's claim: this key genuinely doesn't exist on MySqlConnector's
        // strongly-typed builder, across the oldest stable 2.x release — empirically
        // ArgumentException ("Option 'Allow Multiple Statements' not supported."), not just "some
        // exception". If this test starts failing (no exception, or a different type),
        // MySqlDialect.cs's comment and GetGeneratedKeyPlan() need to be revisited — it would mean
        // AllowMultipleStatements became a real option and CompoundStatement could work for
        // MySqlConnector too.
        var ex = Assert.Throws<ArgumentException>(() =>
            builder["Allow Multiple Statements"] = true);
        Assert.Contains("Allow Multiple Statements", ex.Message);
    }
}
