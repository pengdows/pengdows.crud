using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class FirebirdDialectTests
{
    [Fact]
    public void QuotePrefixSuffix_AreDoubleQuotes()
    {
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird),
            NullLogger<FirebirdDialect>.Instance);
        Assert.Equal("\"", dialect.QuotePrefix);
        Assert.Equal("\"", dialect.QuoteSuffix);
    }

    [Fact]
    public void CreateDbParameter_BooleanMapsToInt16()
    {
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird),
            NullLogger<FirebirdDialect>.Instance);
        var paramTrue = dialect.CreateDbParameter("p", DbType.Boolean, true);
        Assert.Equal(DbType.Int16, paramTrue.DbType);
        Assert.Equal((short)1, paramTrue.Value);

        var paramFalse = dialect.CreateDbParameter("p", DbType.Boolean, false);
        Assert.Equal(DbType.Int16, paramFalse.DbType);
        Assert.Equal((short)0, paramFalse.Value);

        Assert.False(dialect.SupportsJsonTypes);
    }

    /// <summary>
    /// Round-trip test: Guid in Binary mode (default) stores as DbType.Binary / 16 bytes
    /// in RFC 4122 big-endian layout (required by Firebird's CHAR(16) CHARACTER SET OCTETS).
    /// </summary>
    [Fact]
    public void CreateDbParameter_Guid_DefaultsToBinary_BackwardCompatible()
    {
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird),
            NullLogger<FirebirdDialect>.Instance);

        // Verify default mode
        Assert.Equal(FirebirdGuidStorageMode.Binary, dialect.GuidStorageMode);

        var id = Guid.NewGuid();
        var param = dialect.CreateDbParameter("p", DbType.Guid, id);

        Assert.Equal(DbType.Binary, param.DbType);
        var stored = Assert.IsType<byte[]>(param.Value);
        Assert.Equal(16, stored.Length);
        // Bytes are in RFC 4122 big-endian layout; reconstruct Guid by re-applying .NET's
        // mixed-endian convention (Data1/2/3 little-endian) to verify round-trip.
        var roundTripped = new Guid(new byte[]
        {
            stored[3], stored[2], stored[1], stored[0],
            stored[5], stored[4],
            stored[7], stored[6],
            stored[8], stored[9], stored[10], stored[11],
            stored[12], stored[13], stored[14], stored[15]
        });
        Assert.Equal(id, roundTripped);
    }

    /// <summary>
    /// Round-trip test: Guid in String mode stores as DbType.String / hyphenated UUID string.
    /// Opt-in for new schemas that use VARCHAR(36)/CHAR(36) UUID columns.
    /// </summary>
    [Fact]
    public void CreateDbParameter_Guid_StringMode_StoresAsHyphenatedString()
    {
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird),
            NullLogger<FirebirdDialect>.Instance)
        {
            GuidStorageMode = FirebirdGuidStorageMode.String
        };

        var id = Guid.NewGuid();
        var param = dialect.CreateDbParameter("p", DbType.Guid, id);

        Assert.Equal(DbType.String, param.DbType);
        Assert.IsType<string>(param.Value);
        Assert.Equal(id.ToString("D"), (string)param.Value!);
    }

    /// <summary>
    /// Documents that PrepareStatements is false for Firebird because the ADO.NET provider
    /// (FirebirdSql.Data.FirebirdClient) throws "invalid cursor name" or "statement already prepared"
    /// errors when Prepare() is called on parameterized statements — the server-side plan cache
    /// does not cleanly accommodate re-preparation across pool leases with the same handle name.
    /// Tracked for re-evaluation if a future provider version resolves this behavior.
    /// </summary>
    [Fact]
    public void PrepareStatements_IsFalse_BecauseProviderIsOverlyStrictOnPrepare()
    {
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird),
            NullLogger<FirebirdDialect>.Instance);

        Assert.False(dialect.PrepareStatements,
            "Firebird ADO.NET provider throws on Prepare() for parameterized queries when the " +
            "statement handle conflicts with the server-side plan cache across pool leases. " +
            "Execution-time preparation is used instead.");
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void CreateDbParameter_DateTime_PreservesKind(DateTimeKind kind)
    {
        // Firebird TIMESTAMP WITH TIME ZONE requires an explicit timezone (Utc or Local).
        // DateTimeKind.Unspecified causes "Incorrect time zone value" from the provider.
        // CreateDbParameter must NOT normalize Utc/Local to Unspecified for DbType.DateTime.
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird),
            NullLogger<FirebirdDialect>.Instance);
        var dt = DateTime.SpecifyKind(new DateTime(2024, 6, 15, 12, 0, 0), kind);

        var param = dialect.CreateDbParameter("p", DbType.DateTime, dt);

        var stored = Assert.IsType<DateTime>(param.Value);
        Assert.Equal(kind, stored.Kind);
        Assert.Equal(dt.Year, stored.Year);
        Assert.Equal(dt.Hour, stored.Hour);
    }

    // ── Constraint detection ───────────────────────────────────────────────────

    [Fact]
    public void IsUniqueViolation_FirebirdPrimaryKeyMessage_ReturnsTrue()
    {
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird),
            NullLogger<FirebirdDialect>.Instance);
        var ex = new TestDbException(
            "violation of PRIMARY OR UNIQUE KEY constraint \"PK_TEST\" on table \"TEST_TABLE\"");

        Assert.True(dialect.IsUniqueViolation(ex));
    }

    [Fact]
    public void IsUniqueViolation_FirebirdUniqueConstraintMessage_ReturnsTrue()
    {
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird),
            NullLogger<FirebirdDialect>.Instance);
        var ex = new TestDbException(
            "violation of UNIQUE KEY constraint \"UQ_NAME\" on table \"TEST_TABLE\"");

        Assert.True(dialect.IsUniqueViolation(ex));
    }

    [Fact]
    public void IsNotNullViolation_FirebirdValidationErrorMessage_ReturnsTrue()
    {
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird),
            NullLogger<FirebirdDialect>.Instance);
        var ex = new TestDbException(
            "validation error for column \"name\", value \"*** null ***\"");

        Assert.True(dialect.IsNotNullViolation(ex));
    }

    private sealed class TestDbException : System.Data.Common.DbException
    {
        public TestDbException(string message) : base(message) { }
    }
}