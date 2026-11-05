using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// A driver can return a column's NULL as a plain null (not DBNull): a string column reads it as null, not a crash.
/// </summary>
public class NullStringFromValueTests
{
    [Fact]
    public void APlainNullReadsAsANullString()
    {
        Assert.Null(SingleReadColumns.StringFromValue(null!, null));
    }
}
