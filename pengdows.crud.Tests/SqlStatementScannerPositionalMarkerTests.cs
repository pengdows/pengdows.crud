using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// SqlStatementScanner.FindPositionalMarker: the offset of the n-th positional '?' that is a real
/// parameter marker, skipping quoted text and comments the same way HasMultipleStatements does.
/// SAP HANA uses it to find a null array's rendered expression (TYPE-020).
/// </summary>
public sealed class SqlStatementScannerPositionalMarkerTests
{
    [Theory]
    [InlineData("SELECT ?, ?", 0, 7)]
    [InlineData("SELECT ?, ?", 1, 10)]
    [InlineData("SELECT '?', ?", 0, 12)]
    [InlineData("SELECT 'it''s?', ?", 0, 17)]
    [InlineData("SELECT \"a?\", ?", 0, 13)]
    [InlineData("-- why?\nSELECT ?", 0, 15)]
    [InlineData("SELECT /* ? /* ? */ */ ?", 0, 23)]
    [InlineData("SELECT ?", 1, -1)]
    [InlineData("SELECT '?'", 0, -1)]
    public void FindsTheNthMarkerOutsideQuotesAndComments(string sql, int n, int expected)
    {
        Assert.Equal(expected, SqlStatementScanner.FindPositionalMarker(sql, n));
    }
}
