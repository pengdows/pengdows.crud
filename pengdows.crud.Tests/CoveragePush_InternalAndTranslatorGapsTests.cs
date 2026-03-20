// =============================================================================
// FILE: CoveragePush_InternalAndTranslatorGapsTests.cs
// PURPOSE: Coverage boost for uncovered paths in:
//   - InternalConnectionAccessAssertions (AssertIsReadConnection throw path)
// =============================================================================

using System;
using Moq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class CoveragePush_InternalAndTranslatorGapsTests
{
    // =========================================================================
    // InternalConnectionAccessAssertions — AssertIsReadConnection throw path
    // (InternalConnectionAccessAssertions.cs lines 10-11)
    //
    // DatabaseContext.set_ReadWriteMode converts WriteOnly → ReadWrite, so the
    // throw path is only reachable via a mock context that exposes WriteOnly.
    // =========================================================================

    [Fact]
    public void AssertIsReadConnection_WriteOnlyMockContext_Throws()
    {
        var mockCtx = new Mock<IDatabaseContext>();
        mockCtx.Setup(c => c.ReadWriteMode).Returns(ReadWriteMode.WriteOnly);
        mockCtx.Setup(c => c.IsReadOnlyConnection).Returns(false);

        Assert.Throws<InvalidOperationException>(() => mockCtx.Object.AssertIsReadConnection());
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
