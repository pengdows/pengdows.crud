using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using pengdows.crud.metrics;

namespace pengdows.crud.@internal;

internal sealed class MetricsCollector
{
    private readonly MetricsOptions _options;
    private readonly Ewma _commandDuration = new(64);
    private readonly Ewma _connectionHold = new(64);
    private readonly Ewma _transactionDuration = new(32);
    private readonly PercentileRing? _percentileRing;

    private int _connectionsCurrent;
    private int _connectionsMax;
    private long _connectionsOpened;
    private long _connectionsClosed;
    private long _longLivedConnections;

    private long _commandsExecuted;
    private long _commandsFailed;
    private long _commandsTimedOut;
    private long _commandsCancelled;
    private int _maxParametersObserved;
    private long _rowsReadTotal;
    private long _rowsAffectedTotal;
    private long _preparedStatements;
    private long _statementsCached;
    private long _statementsEvicted;

    private int _transactionsActive;
    private int _transactionsMax;

    internal MetricsCollector(MetricsOptions options)
    {
        _options = options ?? MetricsOptions.Default;
        if (_options.EnableApproxPercentiles)
        {
            _percentileRing = new PercentileRing(_options.PercentileWindowSize);
        }
    }

    internal static double ToMilliseconds(long ticks)
    {
        if (ticks <= 0)
        {
            return 0d;
        }

        return ticks * 1000d / Stopwatch.Frequency;
    }

    internal void ConnectionOpened()
    {
        var current = Interlocked.Increment(ref _connectionsCurrent);
        UpdateMax(ref _connectionsMax, current);
        Interlocked.Increment(ref _connectionsOpened);
    }

    internal void ConnectionClosed(double holdDurationMs)
    {
        Decrement(ref _connectionsCurrent);
        Interlocked.Increment(ref _connectionsClosed);
        if (holdDurationMs <= 0d)
        {
            return;
        }

        _connectionHold.AddSample(holdDurationMs);
        if (holdDurationMs >= _options.LongConnectionThreshold.TotalMilliseconds)
        {
            Interlocked.Increment(ref _longLivedConnections);
        }
    }

    internal long CommandStarted(int parameterCount)
    {
        if (parameterCount > 0)
        {
            UpdateMax(ref _maxParametersObserved, parameterCount);
        }

        return Stopwatch.GetTimestamp();
    }

    internal void CommandSucceeded(long startTimestamp, long rowsAffected)
    {
        RecordCommandDuration(startTimestamp, success: true);
        if (rowsAffected > 0)
        {
            Interlocked.Add(ref _rowsAffectedTotal, rowsAffected);
        }

        Interlocked.Increment(ref _commandsExecuted);
    }

    internal void CommandCancelled(long startTimestamp)
    {
        RecordCommandDuration(startTimestamp, success: false);
        Interlocked.Increment(ref _commandsCancelled);
        Interlocked.Increment(ref _commandsFailed);
    }

    internal void CommandTimedOut(long startTimestamp)
    {
        RecordCommandDuration(startTimestamp, success: false);
        Interlocked.Increment(ref _commandsTimedOut);
        Interlocked.Increment(ref _commandsFailed);
    }

    internal void CommandFailed(long startTimestamp)
    {
        RecordCommandDuration(startTimestamp, success: false);
        Interlocked.Increment(ref _commandsFailed);
    }

    internal void RecordRowsRead(long count)
    {
        if (count <= 0)
        {
            return;
        }

        Interlocked.Add(ref _rowsReadTotal, count);
    }

    internal void RecordRowsAffected(long count)
    {
        if (count <= 0)
        {
            return;
        }

        Interlocked.Add(ref _rowsAffectedTotal, count);
    }

    internal void RecordPreparedStatement()
    {
        Interlocked.Increment(ref _preparedStatements);
    }

    internal void RecordStatementCached()
    {
        Interlocked.Increment(ref _statementsCached);
    }

    internal void RecordStatementEvicted(int count)
    {
        if (count <= 0)
        {
            return;
        }

        Interlocked.Add(ref _statementsEvicted, count);
    }

    internal long TransactionStarted()
    {
        var active = Interlocked.Increment(ref _transactionsActive);
        UpdateMax(ref _transactionsMax, active);
        return Stopwatch.GetTimestamp();
    }

    internal void TransactionCompleted(long startTimestamp)
    {
        Decrement(ref _transactionsActive);
        var duration = ToMilliseconds(Stopwatch.GetTimestamp() - startTimestamp);
        if (duration > 0d)
        {
            _transactionDuration.AddSample(duration);
        }
    }

    internal MetricsSnapshot CreateSnapshot()
    {
        var percentiles = _percentileRing?.CreateSnapshot() ?? PercentileSnapshot.Empty;
        return new MetricsSnapshot(
            Interlocked.Read(ref _connectionsOpened),
            Interlocked.Read(ref _connectionsClosed),
            _connectionHold.GetValue(),
            Interlocked.Read(ref _longLivedConnections),
            Interlocked.Read(ref _commandsExecuted),
            Interlocked.Read(ref _commandsFailed),
            Interlocked.Read(ref _commandsTimedOut),
            Interlocked.Read(ref _commandsCancelled),
            _commandDuration.GetValue(),
            percentiles.P95,
            percentiles.P99,
            Volatile.Read(ref _maxParametersObserved),
            Interlocked.Read(ref _rowsReadTotal),
            Interlocked.Read(ref _rowsAffectedTotal),
            Interlocked.Read(ref _preparedStatements),
            Interlocked.Read(ref _statementsCached),
            Interlocked.Read(ref _statementsEvicted),
            Volatile.Read(ref _transactionsActive),
            Volatile.Read(ref _transactionsMax),
            _transactionDuration.GetValue());
    }

    private void RecordCommandDuration(long startTimestamp, bool success)
    {
        if (startTimestamp == 0)
        {
            return;
        }

        var elapsed = ToMilliseconds(Stopwatch.GetTimestamp() - startTimestamp);
        if (elapsed <= 0d)
        {
            return;
        }

        _commandDuration.AddSample(elapsed);
        if (success)
        {
            _percentileRing?.Add(elapsed);
        }
    }

    private static void UpdateMax(ref int target, int candidate)
    {
        int current;
        while (candidate > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, candidate, current) == current)
            {
                break;
            }
        }
    }

    private static void Decrement(ref int field)
    {
        int current;
        do
        {
            current = Volatile.Read(ref field);
            if (current == 0)
            {
                return;
            }
        } while (Interlocked.CompareExchange(ref field, current - 1, current) != current);
    }

    internal readonly struct MetricsSnapshot(
        long ConnectionsOpened,
        long ConnectionsClosed,
        double AvgConnectionHoldMs,
        long LongLivedConnections,
        long CommandsExecuted,
        long CommandsFailed,
        long CommandsTimedOut,
        long CommandsCancelled,
        double AvgCommandMs,
        double P95CommandMs,
        double P99CommandMs,
        int MaxParametersObserved,
        long RowsReadTotal,
        long RowsAffectedTotal,
        long PreparedStatements,
        long StatementsCached,
        long StatementsEvicted,
        int TransactionsActive,
        int TransactionsMax,
        double AvgTransactionMs)
    {
        public long ConnectionsOpened { get; } = ConnectionsOpened;
        public long ConnectionsClosed { get; } = ConnectionsClosed;
        public double AvgConnectionHoldMs { get; } = AvgConnectionHoldMs;
        public long LongLivedConnections { get; } = LongLivedConnections;
        public long CommandsExecuted { get; } = CommandsExecuted;
        public long CommandsFailed { get; } = CommandsFailed;
        public long CommandsTimedOut { get; } = CommandsTimedOut;
        public long CommandsCancelled { get; } = CommandsCancelled;
        public double AvgCommandMs { get; } = AvgCommandMs;
        public double P95CommandMs { get; } = P95CommandMs;
        public double P99CommandMs { get; } = P99CommandMs;
        public int MaxParametersObserved { get; } = MaxParametersObserved;
        public long RowsReadTotal { get; } = RowsReadTotal;
        public long RowsAffectedTotal { get; } = RowsAffectedTotal;
        public long PreparedStatements { get; } = PreparedStatements;
        public long StatementsCached { get; } = StatementsCached;
        public long StatementsEvicted { get; } = StatementsEvicted;
        public int TransactionsActive { get; } = TransactionsActive;
        public int TransactionsMax { get; } = TransactionsMax;
        public double AvgTransactionMs { get; } = AvgTransactionMs;
    }

    private sealed class Ewma
    {
        private readonly double _alpha;
        private double _value;
        private int _initialized;

        internal Ewma(int halfLife)
        {
            if (halfLife <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(halfLife));
            }

            _alpha = 1d - Math.Pow(0.5d, 1d / halfLife);
        }

        internal void AddSample(double sample)
        {
            if (double.IsNaN(sample) || double.IsInfinity(sample))
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
            {
                Volatile.Write(ref _value, sample);
                return;
            }

            while (true)
            {
                var current = Volatile.Read(ref _value);
                var updated = current + _alpha * (sample - current);
                if (Interlocked.CompareExchange(ref _value, updated, current) == current)
                {
                    break;
                }
            }
        }

        internal double GetValue()
        {
            if (Volatile.Read(ref _initialized) == 0)
            {
                return 0d;
            }

            return Volatile.Read(ref _value);
        }
    }

    private sealed class PercentileRing
    {
        private readonly double[] _buffer;
        private readonly int _mask;
        private long _index;
        private long _count;

        internal PercentileRing(int size)
        {
            _buffer = new double[size];
            _mask = size - 1;
        }

        internal void Add(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return;
            }

            var slot = Interlocked.Increment(ref _index) - 1;
            var position = (int)(slot & _mask);
            Volatile.Write(ref _buffer[position], value);

            long current;
            while ((current = Volatile.Read(ref _count)) < _buffer.Length)
            {
                if (Interlocked.CompareExchange(ref _count, current + 1, current) == current)
                {
                    break;
                }
            }
        }

        internal PercentileSnapshot CreateSnapshot()
        {
            var count = (int)Math.Min(Volatile.Read(ref _count), _buffer.Length);
            if (count == 0)
            {
                return PercentileSnapshot.Empty;
            }

            var snapshot = new double[count];
            var start = Math.Max(0L, Volatile.Read(ref _index) - count);
            for (var i = 0; i < count; i++)
            {
                var idx = (int)((start + i) & _mask);
                snapshot[i] = Volatile.Read(ref _buffer[idx]);
            }

            Array.Sort(snapshot);
            var p95 = GetPercentile(snapshot, 0.95d);
            var p99 = GetPercentile(snapshot, 0.99d);
            return new PercentileSnapshot(p95, p99);
        }

        private static double GetPercentile(IReadOnlyList<double> data, double percentile)
        {
            if (data.Count == 0)
            {
                return 0d;
            }

            if (data.Count == 1)
            {
                return data[0];
            }

            var position = percentile * (data.Count - 1);
            var lowerIndex = (int)Math.Floor(position);
            var upperIndex = (int)Math.Ceiling(position);
            if (lowerIndex == upperIndex)
            {
                return data[lowerIndex];
            }

            var weight = position - lowerIndex;
            return data[lowerIndex] + (data[upperIndex] - data[lowerIndex]) * weight;
        }
    }

    internal readonly struct PercentileSnapshot(double p95, double p99)
    {
        public static readonly PercentileSnapshot Empty = new(0d, 0d);
        public double P95 { get; } = p95;
        public double P99 { get; } = p99;
    }
}
