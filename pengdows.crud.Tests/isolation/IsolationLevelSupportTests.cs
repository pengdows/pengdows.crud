#region

using System;
using System.Collections.Generic;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud.isolation;
using Xunit;

#endregion

namespace pengdows.crud.Tests.isolation;

public class IsolationLevelSupportTests
{
    [Fact]
    public void Validate_AllSupportedLevels_DoesNotThrow()
    {
        var validator = new IsolationLevelSupport();
        var supported = new Dictionary<SupportedDatabase, IsolationLevel[]>
        {
            [SupportedDatabase.SqlServer] = new[]
            {
                IsolationLevel.ReadUncommitted,
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable,
                IsolationLevel.Snapshot
            },
            [SupportedDatabase.PostgreSql] = new[]
            {
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable
            },
            [SupportedDatabase.MySql] = new[]
            {
                IsolationLevel.ReadUncommitted,
                IsolationLevel.ReadCommitted,
                IsolationLevel.RepeatableRead,
                IsolationLevel.Serializable
            },
            [SupportedDatabase.DuckDB] = new[]
            {
                IsolationLevel.Serializable
            }
        };

        foreach (var kv in supported)
        {
            foreach (var lvl in kv.Value)
            {
                validator.Validate(kv.Key, lvl);
            }
        }
    }

    [Fact]
    public void Validate_UnsupportedLevel_Throws()
    {
        var validator = new IsolationLevelSupport();
        Assert.Throws<InvalidOperationException>(() =>
            validator.Validate(SupportedDatabase.SqlServer, IsolationLevel.Chaos));
        Assert.Throws<InvalidOperationException>(() =>
            validator.Validate(SupportedDatabase.DuckDB, IsolationLevel.ReadCommitted));
    }

    [Fact]
    public void Validate_UnknownDatabase_Throws()
    {
        var validator = new IsolationLevelSupport();
        Assert.Throws<NotSupportedException>(() =>
            validator.Validate(SupportedDatabase.Unknown, IsolationLevel.ReadCommitted));
    }
}
