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
    /// Round-trip test: Guid in Binary mode (default) stores as DbType.Binary / byte[].
    /// This is the backward-compatible default for existing Firebird schemas
    /// that use CHAR(16) OCTETS columns.
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
        Assert.IsType<byte[]>(param.Value);
        Assert.Equal(id.ToByteArray(), (byte[])param.Value!);
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
}