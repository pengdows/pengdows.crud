using Xunit;

namespace pengdows.crud.Tests;

public sealed class SharedProviderPoolBudgetSplitTests
{
    [Theory]
    [InlineData(10, 10, 100, 10, 10)]
    [InlineData(10, 10, 20, 10, 10)]
    [InlineData(20, 20, 22, 11, 11)]
    [InlineData(30, 10, 20, 15, 5)]
    [InlineData(1, 40, 22, 1, 21)]
    [InlineData(40, 1, 22, 21, 1)]
    [InlineData(20, 20, 2, 1, 1)]
    [InlineData(20, 20, 3, 1, 2)]
    public void Split_DividesAnOversubscribedBudgetAndKeepsBothPoolsUsable(
        int readerRequested, int writerRequested, int sharedLimit, int expectedReader, int expectedWriter)
    {
        var (reader, writer) = DatabaseContext.SplitSharedProviderPoolBudget(readerRequested, writerRequested, sharedLimit);

        Assert.Equal(expectedReader, reader);
        Assert.Equal(expectedWriter, writer);
        Assert.InRange(reader, 1, readerRequested);
        Assert.InRange(writer, 1, writerRequested);
    }

    [Theory]
    [InlineData(20, 20, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(5, 1, 1)]
    public void Split_WhenTheSharedLimitCannotGiveEachPoolASlot_DoesNotThrowAndNeverExceedsTheLimitPerPool(
        int readerRequested, int writerRequested, int sharedLimit)
    {
        var (reader, writer) = DatabaseContext.SplitSharedProviderPoolBudget(readerRequested, writerRequested, sharedLimit);

        Assert.InRange(reader, 1, readerRequested);
        Assert.InRange(writer, 1, writerRequested);
        Assert.True(reader <= sharedLimit && writer <= sharedLimit);
    }

    [Theory]
    [InlineData(0, 10, 5)]
    [InlineData(10, 0, 5)]
    public void Split_LeavesAForbiddenPoolUntouched(int readerRequested, int writerRequested, int sharedLimit)
    {
        var (reader, writer) = DatabaseContext.SplitSharedProviderPoolBudget(readerRequested, writerRequested, sharedLimit);

        Assert.Equal(readerRequested, reader);
        Assert.Equal(writerRequested, writer);
    }

    [Fact]
    public void Split_HandlesRequestsNearIntMaxWithoutOverflow()
    {
        var (reader, writer) = DatabaseContext.SplitSharedProviderPoolBudget(int.MaxValue, int.MaxValue, 100);

        Assert.InRange(reader, 1, 100);
        Assert.InRange(writer, 1, 100);
        Assert.True(reader + writer <= 100);
    }
}
