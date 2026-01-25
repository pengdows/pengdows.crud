#region

using pengdows.crud.dialects;
using pengdows.crud.enums;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

public class DuckDbUpsertColumnTests
{
    [Fact]
    public void UpsertIncomingColumn_UsesExcludedAlias()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var dialect = new DuckDbDialect(factory, NullLogger.Instance);

        var result = dialect.UpsertIncomingColumn("name");

        Assert.Equal("EXCLUDED.\"name\"", result);
    }
}