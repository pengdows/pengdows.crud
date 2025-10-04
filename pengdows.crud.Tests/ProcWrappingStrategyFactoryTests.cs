using pengdows.crud.enums;
using pengdows.crud.strategies.proc;
using Xunit;

namespace pengdows.crud.Tests;

public class ProcWrappingStrategyFactoryTests
{
    [Fact]
    public void Create_ReturnsExpectedStrategyTypes()
    {
        Assert.IsType<ExecProcWrappingStrategy>(ProcWrappingStrategyFactory.Create(ProcWrappingStyle.Exec));
        Assert.IsType<CallProcWrappingStrategy>(ProcWrappingStrategyFactory.Create(ProcWrappingStyle.Call));
        Assert.IsType<PostgresProcWrappingStrategy>(ProcWrappingStrategyFactory.Create(ProcWrappingStyle.PostgreSQL));
        Assert.IsType<OracleProcWrappingStrategy>(ProcWrappingStrategyFactory.Create(ProcWrappingStyle.Oracle));
        Assert.IsType<ExecuteProcedureWrappingStrategy>(ProcWrappingStrategyFactory.Create(ProcWrappingStyle.ExecuteProcedure));
        Assert.IsType<UnsupportedProcWrappingStrategy>(ProcWrappingStrategyFactory.Create(ProcWrappingStyle.None));
        // Unknown values fallback to Unsupported
        Assert.IsType<UnsupportedProcWrappingStrategy>(ProcWrappingStrategyFactory.Create((ProcWrappingStyle)999));
    }
}

