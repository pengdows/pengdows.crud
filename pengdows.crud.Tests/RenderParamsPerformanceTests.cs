#region

using System.Data;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for RenderParams method to ensure Span-based optimization maintains correct behavior.
/// </summary>
public class RenderParamsPerformanceTests : SqlLiteContextTestBase
{
    [Fact]
    public void RenderParams_SingleParameter_ReplacesCorrectly()
    {
        using var container = Context.CreateSqlContainer() as SqlContainer;
        container!.Query.Append("SELECT * FROM users WHERE id = {P}userId");

        var rendered = container.RenderParams(container.Query.ToString());

        Assert.Contains("@userId", rendered);
        Assert.Single(container.ParamSequence);
        Assert.Equal("userId", container.ParamSequence[0]);
    }

    [Fact]
    public void RenderParams_MultipleParameters_PreservesOrder()
    {
        using var container = Context.CreateSqlContainer() as SqlContainer;
        container!.Query.Append("SELECT * FROM orders WHERE userId = {P}userId AND status = {P}status AND date > {P}minDate");

        var rendered = container.RenderParams(container.Query.ToString());

        Assert.Contains("@userId", rendered);
        Assert.Contains("@status", rendered);
        Assert.Contains("@minDate", rendered);
        Assert.Equal(3, container.ParamSequence.Count);
        Assert.Equal(new[] { "userId", "status", "minDate" }, container.ParamSequence.ToArray());
    }

    [Fact]
    public void RenderParams_NoParameters_ReturnsUnchanged()
    {
        using var container = Context.CreateSqlContainer() as SqlContainer;
        var sql = "SELECT * FROM users";
        container!.Query.Append(sql);

        var rendered = container.RenderParams(container.Query.ToString());

        Assert.Equal(sql, rendered);
        Assert.Empty(container.ParamSequence);
    }

    [Fact]
    public void RenderParams_ParameterWithUnderscores_HandlesCorrectly()
    {
        using var container = Context.CreateSqlContainer() as SqlContainer;
        container!.Query.Append("SELECT * FROM users WHERE user_id = {P}user_id AND created_at = {P}created_at");

        var rendered = container.RenderParams(container.Query.ToString());

        Assert.Contains("@user_id", rendered);
        Assert.Contains("@created_at", rendered);
        Assert.Equal(new[] { "user_id", "created_at" }, container.ParamSequence.ToArray());
    }

    [Fact]
    public void RenderParams_ParameterWithDigits_HandlesCorrectly()
    {
        using var container = Context.CreateSqlContainer() as SqlContainer;
        container!.Query.Append("SELECT * FROM data WHERE p1 = {P}param1 AND p2 = {P}param2");

        var rendered = container.RenderParams(container.Query.ToString());

        Assert.Contains("@param1", rendered);
        Assert.Contains("@param2", rendered);
        Assert.Equal(new[] { "param1", "param2" }, container.ParamSequence.ToArray());
    }

    [Fact]
    public void RenderParams_ParameterAtStart_HandlesCorrectly()
    {
        using var container = Context.CreateSqlContainer() as SqlContainer;
        container!.Query.Append("{P}userId = 123");

        var rendered = container.RenderParams(container.Query.ToString());

        Assert.StartsWith("@userId", rendered);
        Assert.Single(container.ParamSequence);
    }

    [Fact]
    public void RenderParams_ParameterAtEnd_HandlesCorrectly()
    {
        using var container = Context.CreateSqlContainer() as SqlContainer;
        container!.Query.Append("SELECT * FROM users WHERE id = {P}userId");

        var rendered = container.RenderParams(container.Query.ToString());

        Assert.EndsWith("@userId", rendered);
        Assert.Single(container.ParamSequence);
    }

    [Fact]
    public void RenderParams_ConsecutiveParameters_HandlesCorrectly()
    {
        using var container = Context.CreateSqlContainer() as SqlContainer;
        container!.Query.Append("VALUES ({P}a, {P}b, {P}c)");

        var rendered = container.RenderParams(container.Query.ToString());

        Assert.Contains("@a, @b, @c", rendered);
        Assert.Equal(new[] { "a", "b", "c" }, container.ParamSequence.ToArray());
    }

    [Fact]
    public void RenderParams_LongParameterName_HandlesCorrectly()
    {
        using var container = Context.CreateSqlContainer() as SqlContainer;
        var longName = "very_long_parameter_name_with_many_characters_" + new string('x', 50);
        container!.Query.Append($"SELECT * FROM users WHERE field = {{P}}{longName}");

        var rendered = container.RenderParams(container.Query.ToString());

        Assert.Contains($"@{longName}", rendered);
        Assert.Single(container.ParamSequence);
        Assert.Equal(longName, container.ParamSequence[0]);
    }

    [Fact]
    public void RenderParams_DuplicateParameterNames_RecordsMultipleTimes()
    {
        using var container = Context.CreateSqlContainer() as SqlContainer;
        container!.Query.Append("SELECT * FROM users WHERE id = {P}userId OR parent_id = {P}userId");

        var rendered = container.RenderParams(container.Query.ToString());

        // Both occurrences should be replaced
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(rendered, "@userId").Count);
        // ParamSequence should record both occurrences
        Assert.Equal(2, container.ParamSequence.Count);
        Assert.All(container.ParamSequence, name => Assert.Equal("userId", name));
    }

    [Fact]
    public void RenderParams_MixedTextAndParameters_PreservesText()
    {
        using var container = Context.CreateSqlContainer() as SqlContainer;
        var sql = "INSERT INTO log (message, user_id, timestamp) VALUES ('Action performed', {P}userId, {P}timestamp)";
        container!.Query.Append(sql);

        var rendered = container.RenderParams(container.Query.ToString());

        // Text should be preserved
        Assert.Contains("'Action performed'", rendered);
        Assert.Contains("@userId", rendered);
        Assert.Contains("@timestamp", rendered);
        Assert.Equal(new[] { "userId", "timestamp" }, container.ParamSequence.ToArray());
    }

    [Fact]
    public void RenderParams_CurlyBracesWithoutP_NotReplaced()
    {
        using var container = Context.CreateSqlContainer() as SqlContainer;
        container!.Query.Append("SELECT '{\"key\": \"value\"}' as json, {P}param as param");

        var rendered = container.RenderParams(container.Query.ToString());

        // JSON-like text should remain unchanged
        Assert.Contains("{\"key\": \"value\"}", rendered);
        // But parameter should be replaced
        Assert.Contains("@param", rendered);
        Assert.Single(container.ParamSequence);
    }

    [Fact]
    public void RenderParams_HighVolumeParameters_HandlesCorrectly()
    {
        using var container = Context.CreateSqlContainer() as SqlContainer;

        // Build a query with 50 parameters
        container!.Query.Append("INSERT INTO data VALUES (");
        for (int i = 0; i < 50; i++)
        {
            if (i > 0) container.Query.Append(", ");
            container.Query.Append($"{{P}}p{i}");
        }
        container.Query.Append(")");

        var rendered = container.RenderParams(container.Query.ToString());

        // Verify all parameters were replaced
        for (int i = 0; i < 50; i++)
        {
            Assert.Contains($"@p{i}", rendered);
        }
        Assert.Equal(50, container.ParamSequence.Count);
    }

    [Fact]
    public void RenderParams_EmptyParameterName_NotReplaced()
    {
        using var container = Context.CreateSqlContainer() as SqlContainer;
        container!.Query.Append("SELECT * FROM users WHERE {P} AND id = {P}userId");

        var rendered = container.RenderParams(container.Query.ToString());

        // Empty parameter name should be left as-is or skipped
        // Valid parameter should still be replaced
        Assert.Contains("@userId", rendered);
    }
}
