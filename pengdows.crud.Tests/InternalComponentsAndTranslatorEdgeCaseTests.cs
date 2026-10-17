// =============================================================================
// FILE: InternalComponentsAndTranslatorEdgeCaseTests.cs
// PURPOSE: Coverage boost for uncovered paths in:
//   - InternalConnectionAccessAssertions (AssertIsReadConnection throw path)
// =============================================================================

using System;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class InternalComponentsAndTranslatorEdgeCaseTests
{
    // =========================================================================
    // InternalConnectionAccessAssertions — AssertIsReadConnection throw path
    // (InternalConnectionAccessAssertions.cs lines 10-11)
    //
    // DatabaseContext.set_ReadWriteMode converts WriteOnly → ReadWrite, so the
    // throw path is only reachable via a mock context that exposes WriteOnly.
    // =========================================================================

    [Fact]
    public void AssertIsReadConnection_WriteOnlyContext_Throws()
    {
        var ctx = new ScriptedDatabaseContext { ReadWriteMode = ReadWriteMode.WriteOnly, IsReadOnlyConnection = false };

        Assert.Throws<InvalidOperationException>(() => ctx.AssertIsReadConnection());
    }

    [Fact]
    public void AssertIsReadConnection_ReadOnlyContext_DoesNotThrow()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite));

        // Must not throw — ReadOnly has the ReadOnly bit set
        ctx.AssertIsReadConnection();
    }
}
