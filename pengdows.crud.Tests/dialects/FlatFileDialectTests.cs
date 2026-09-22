using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Scoped to the pool-separation/read-only findings from
/// docs/connection/new-database-pooling-appname-readonly-audit.md's FlatFile section — not a
/// full FlatFileDialect capability suite (no dedicated one exists on this branch yet).
/// </summary>
public class FlatFileDialectTests
{
    private static FlatFileDialect Dialect() =>
        new(new fakeDbFactory(SupportedDatabase.FlatFile), NullLogger<FlatFileDialect>.Instance);

    [Fact]
    public void ApplicationNameSettingName_IsFlatFilesRealKeyword()
    {
        // Confirmed via pengdows.flatfile/FlatFileConnectionStringBuilder.cs's own
        // KeyApplicationName constant — a real, recognized keyword, not a guess.
        Assert.Equal("applicationName", Dialect().ApplicationNameSettingName);
    }

    [Fact]
    public void SupportsExternalPooling_IsFalse()
    {
        // FlatFile is a custom, in-process, file-based provider with no network handshake and no
        // real connection pool to configure — architecturally identical to why
        // DuckDbDialect.SupportsExternalPooling is false.
        Assert.False(Dialect().SupportsExternalPooling);
    }

    [Fact]
    public void GetReadOnlyConnectionParameter_IsFlatFilesRealReadOnlyKeyword()
    {
        // Confirmed via pengdows.flatfile/FlatFileConnectionStringBuilder.cs's own KeyReadOnly
        // constant and ReadOnly property (SetOrRemove(KeyReadOnly, value ? "true" : null)) — a
        // real, hard-enforced keyword: "any mutating statement (DML/DDL) is rejected immediately".
        Assert.Equal("readonly=true", Dialect().GetReadOnlyConnectionParameter());
    }
}
