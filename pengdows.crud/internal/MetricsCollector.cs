// =============================================================================
// FILE: MetricsCollector.cs
// PURPOSE: Collects performance metrics for database operations.
//
// AI SUMMARY:
// - Central metrics collection for connections, commands, and transactions.
// - Thread-safe: uses Interlocked/Volatile for all counters.
// - Connection metrics:
//   * ConnectionOpened/Closed, current/max counts
//   * Connection hold duration (EWMA), open/close duration
//   * Long-lived connection tracking (threshold-based)
// - Command metrics:
//   * Commands executed/failed/timed out/cancelled
//   * Average command duration (EWMA), P95/P99 percentiles
//   * Max parameters observed, rows read/affected
//   * Prepared statements, statement cache hits/evictions
// - Transaction metrics:
//   * Active/max transactions, average duration
// - EWMA (Exponentially Weighted Moving Average): smoothed averages.
// - PercentileRing: circular buffer for approximate P95/P99 calculation.
// - MetricsSnapshot: immutable point-in-time metrics capture.
// - MetricsChanged event: notifies subscribers of metric updates.
// - ToMilliseconds(): Converts Stopwatch ticks to milliseconds.
// =============================================================================

using System.Diagnostics;
using pengdows.crud.metrics;

namespace pengdows.crud.@internal;

internal sealed class MetricsCollector
{
    private readonly MetricsOptions _options;
    private readonly MetricsCollector? _parent;
    private readonly Ewma _commandDuration = new(64);
    private readonly Ewma _connectionHold = new(64);
    private readonly Ewma _connectionOpenDuration = new(64);
    private readonly Ewma _connectionCloseDuration = new(64);
    private readonly Ewma _transactionDuration = new(32);
    private readonly PercentileRing? _percentileRing;
    private Action? _metricsChanged;

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

    internal MetricsCollector(MetricsOptions options, MetricsCollector? parent = null)
    {
        _options = options ?? MetricsOptions.Default;
        _parent = parent;
        if (_options.EnableApproxPercentiles)
        {
            _percentileRing = new PercentileRing(_options.PercentileWindowSize);
        }
    }

    internal event Action MetricsChanged
    {
        add => AddHandler(ref _metricsChanged, value);
        remove => RemoveHandler(ref _metricsChanged, value);
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
        _parent?.ConnectionOpened();
        var current = Interlocked.Increment(ref _connectionsCurrent);
        UpdateMax(ref _connectionsMax, current);
        Interlocked.Increment(ref _connectionsOpened);
        NotifyUpdated();
    }

    internal void ConnectionClosed(double holdDurationMs)
    {
        _parent?.ConnectionClosed(holdDurationMs);
        Decrement(ref _connectionsCurrent);
        Interlocked.Increment(ref _connectionsClosed);
        if (holdDurationMs > 0d)
        {
            _connectionHold.AddSample(holdDurationMs);
            if (holdDurationMs >= _options.LongConnectionThreshold.TotalMilliseconds)
            {
                Interlocked.Increment(ref _longLivedConnections);
            }
        }

        NotifyUpdated();
    }

    internal void RecordConnectionOpenDuration(double durationMs)
    {
        _parent?.RecordConnectionOpenDuration(durationMs);
        if (durationMs <= 0d)
        {
            return;
        }

        _connectionOpenDuration.AddSample(durationMs);
        NotifyUpdated();
    }

    internal void RecordConnectionCloseDuration(double durationMs)
    {
        _parent?.RecordConnectionCloseDuration(durationMs);
        if (durationMs <= 0d)
        {
            return;
        }

        _connectionCloseDuration.AddSample(durationMs);
        NotifyUpdated();
    }

    internal long CommandStarted(int parameterCount)
    {
        _parent?.CommandStarted(parameterCount);
        if (parameterCount > 0)
        {
            var previous = Volatile.Read(ref _maxParametersObserved);
            UpdateMax(ref _maxParametersObserved, parameterCount);
            if (parameterCount > previous)
            {
                NotifyUpdated();
            }
        }

        return Stopwatch.GetTimestamp();
    }

    internal void CommandSucceeded(long startTimestamp, long rowsAffected)
    {
        _parent?.CommandSucceeded(startTimestamp, rowsAffected);
        RecordCommandDuration(startTimestamp, true);
        if (rowsAffected > 0)
        {
            Interlocked.Add(ref _rowsAffectedTotal, rowsAffected);
        }

        Interlocked.Increment(ref _commandsExecuted);
        NotifyUpdated();
    }

    internal void CommandCancelled(long startTimestamp)
    {
        _parent?.CommandCancelled(startTimestamp);
        RecordCommandDuration(startTimestamp, false);
        Interlocked.Increment(ref _commandsCancelled);
        Interlocked.Increment(ref _commandsFailed);
        NotifyUpdated();
    }

    internal void CommandTimedOut(long startTimestamp)
    {
        _parent?.CommandTimedOut(startTimestamp);
        RecordCommandDuration(startTimestamp, false);
        Interlocked.Increment(ref _commandsTimedOut);
        Interlocked.Increment(ref _commandsFailed);
        NotifyUpdated();
    }

    internal void CommandFailed(long startTimestamp)
    {
        _parent?.CommandFailed(startTimestamp);
        RecordCommandDuration(startTimestamp, false);
        Interlocked.Increment(ref _commandsFailed);
        NotifyUpdated();
    }

    internal void RecordRowsRead(long count)
    {
        _parent?.RecordRowsRead(count);
        if (count <= 0)
        {
            return;
        }

        Interlocked.Add(ref _rowsReadTotal, count);
        NotifyUpdated();
    }

    internal void RecordRowsAffected(long count)
    {
        _parent?.RecordRowsAffected(count);
        if (count <= 0)
        {
            return;
        }

        Interlocked.Add(ref _rowsAffectedTotal, count);
        NotifyUpdated();
    }

    internal void RecordPreparedStatement()
    {
        _parent?.RecordPreparedStatement();
        Interlocked.Increment(ref _preparedStatements);
        NotifyUpdated();
    }

    internal void RecordStatementCached()
    {
        _parent?.RecordStatementCached();
        Interlocked.Increment(ref _statementsCached);
        NotifyUpdated();
    }

    internal void RecordStatementEvicted(int count)
    {
        _parent?.RecordStatementEvicted(count);
        if (count <= 0)
        {
            return;
        }

        Interlocked.Add(ref _statementsEvicted, count);
        NotifyUpdated();
    }

    internal long TransactionStarted()
    {
        _parent?.TransactionStarted();
        var active = Interlocked.Increment(ref _transactionsActive);
        UpdateMax(ref _transactionsMax, active);
        NotifyUpdated();
        return Stopwatch.GetTimestamp();
    }

    internal void TransactionCompleted(long startTimestamp)
    {
        _parent?.TransactionCompleted(startTimestamp);
        Decrement(ref _transactionsActive);
        var duration = ToMilliseconds(Stopwatch.GetTimestamp() - startTimestamp);
        if (duration > 0d)
        {
            _transactionDuration.AddSample(duration);
        }

        NotifyUpdated();
    }

    internal MetricsSnapshot CreateSnapshot()
    {
        var percentiles = _percentileRing?.CreateSnapshot() ?? PercentileSnapshot.Empty;
        return new MetricsSnapshot(
            Volatile.Read(ref _connectionsCurrent),
            Volatile.Read(ref _connectionsMax),
            Interlocked.Read(ref _connectionsOpened),
            Interlocked.Read(ref _connectionsClosed),
            _connectionHold.GetValue(),
            _connectionOpenDuration.GetValue(),
            _connectionCloseDuration.GetValue(),
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

    private static void AddHandler(ref Action? field, Action handler)
    {
        if (handler == null)
        {
            return;
        }

        Action? current;
        Action? updated;
        do
        {
            current = Volatile.Read(ref field);
            updated = (Action?)Delegate.Combine(current, handler);
        } while (Interlocked.CompareExchange(ref field, updated, current) != current);
    }

    private static void RemoveHandler(ref Action? field, Action handler)
    {
        if (handler == null)
        {
            return;
        }

        Action? current;
        Action? updated;
        do
        {
            current = Volatile.Read(ref field);
            updated = (Action?)Delegate.Remove(current, handler);
        } while (Interlocked.CompareExchange(ref field, updated, current) != current);
    }

    private void NotifyUpdated()
    {
        var handler = Volatile.Read(ref _metricsChanged);
        handler?.Invoke();
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
        int ConnectionsCurrent,
        int PeakOpenConnections,
        long ConnectionsOpened,
        long ConnectionsClosed,
        double AvgConnectionHoldMs,
        double AvgConnectionOpenMs,
        double AvgConnectionCloseMs,
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
        public int ConnectionsCurrent { get; } = ConnectionsCurrent;
        public int PeakOpenConnections { get; } = PeakOpenConnections;
        public long ConnectionsOpened { get; } = ConnectionsOpened;
        public long ConnectionsClosed { get; } = ConnectionsClosed;
        public double AvgConnectionHoldMs { get; } = AvgConnectionHoldMs;
        public double AvgConnectionOpenMs { get; } = AvgConnectionOpenMs;
        public double AvgConnectionCloseMs { get; } = AvgConnectionCloseMs;
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
