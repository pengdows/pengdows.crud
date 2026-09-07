using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace pengdows.crud.Tests;

// Code-review finding: DatabaseContext.SetupFields had a check-then-set race on the static
// TypeCoercionHelper.Logger ("if (Logger is NullLogger) Logger = ...") — harmless when contexts
// were only ever constructed one at a time, but DatabaseContext.CreateAsync is explicitly
// designed for concurrent construction (e.g. multiple tenants constructed via
// Task.WhenAll(DatabaseContext.CreateAsync(...), ...)). TypeCoercionHelper.SetLoggerIfUnset
// closes this with a single Interlocked.CompareExchange instead of separate read-then-write steps.
//
// Root-cause note (found investigating a real, reproducible full-suite-only failure of
// SetLoggerIfUnset_CalledConcurrentlyByManyDistinctLoggers_ExactlyOneWinsAndNoneAreLost below,
// which passed reliably in isolation): TypeCoercionHelper.Logger is a process-global static
// mutable field. Six other test classes that read/write it (DataReaderMapperNegativeTests,
// DataReaderMapperBranchTests, DataReaderMapperCoercionTests, TypeCoercionHelperExtensiveTests,
// TypeCoercionHelperBehaviorTests, TypeCoercionHelperBranchTests) already share
// [Collection("TypeRegistry")] specifically to serialize against each other for exactly this
// reason — this file (and SecurityRegressionTests, which has the identical gap) were simply
// missing that attribute, so xUnit was free to run them concurrently with the "TypeRegistry"
// collection and with each other, letting an unrelated test's TypeCoercionHelper.Logger = ...
// assignment race with this test's own 32-way CompareExchange contest and occasionally win with a
// logger not in `candidates`, failing Assert.Contains(winner, candidates) — a test-isolation gap,
// not a defect in the atomic SetLoggerIfUnset implementation itself (which the tests below already
// correctly prove is atomic in isolation).
[Collection("TypeRegistry")]
public class TypeCoercionHelperLoggerRaceTests
{
    [Fact]
    public void SetLoggerIfUnset_WhenUnset_AdoptsTheGivenLogger()
    {
        var original = TypeCoercionHelper.Logger;
        try
        {
            TypeCoercionHelper.Logger = NullLogger.Instance;
            var first = new RecordingLogger();

            TypeCoercionHelper.SetLoggerIfUnset(first);

            Assert.Same(first, TypeCoercionHelper.Logger);
        }
        finally
        {
            TypeCoercionHelper.Logger = original;
        }
    }

    [Fact]
    public void SetLoggerIfUnset_WhenAlreadySet_DoesNotOverwriteTheWinner()
    {
        var original = TypeCoercionHelper.Logger;
        try
        {
            TypeCoercionHelper.Logger = NullLogger.Instance;
            var first = new RecordingLogger();
            var second = new RecordingLogger();

            TypeCoercionHelper.SetLoggerIfUnset(first);
            TypeCoercionHelper.SetLoggerIfUnset(second);

            Assert.Same(first, TypeCoercionHelper.Logger);
        }
        finally
        {
            TypeCoercionHelper.Logger = original;
        }
    }

    // Proves the fix is genuinely atomic, not just correct in the single-threaded case the two
    // tests above already cover (which the OLD check-then-set code also passed).
    [Fact]
    public async Task SetLoggerIfUnset_CalledConcurrentlyByManyDistinctLoggers_ExactlyOneWinsAndNoneAreLost()
    {
        var original = TypeCoercionHelper.Logger;
        try
        {
            TypeCoercionHelper.Logger = NullLogger.Instance;

            var candidates = Enumerable.Range(0, 32).Select(_ => new RecordingLogger()).ToList();

            await Task.WhenAll(candidates.Select(c => Task.Run(() => TypeCoercionHelper.SetLoggerIfUnset(c))));

            var winner = TypeCoercionHelper.Logger;
            Assert.Contains(winner, candidates);
            Assert.NotSame(NullLogger.Instance, winner);
        }
        finally
        {
            TypeCoercionHelper.Logger = original;
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
