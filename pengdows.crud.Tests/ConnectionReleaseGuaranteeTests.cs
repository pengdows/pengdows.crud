using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The release guarantee, one failure path per test: whatever the provider throws, and wherever, the pool slot goes
/// back to the governor and the provider connection is disposed. Each test runs on a real <see cref="DatabaseContext"/>
/// (Standard mode) over fakeDb, arms one failure on the connections opened after construction, and checks both pools
/// end with nothing in use and nothing open. docs/connection/release-guarantees.md cites these tests by name.
/// </summary>
public sealed class ConnectionReleaseGuaranteeTests
{
    private const string StandardPostgreSql = "Standard/PostgreSql";
    private const string SingleWriterSqlite = "SingleWriter/Sqlite";

    // Standard opens a pooled connection per operation; SingleWriter on a file database serializes writes through its
    // turnstile governor. Both hand out ephemeral connections, so both must give every one back.
    public static TheoryData<string> Modes => new() { StandardPostgreSql, SingleWriterSqlite };

    [Table("items")]
    private sealed class ReleaseItem
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class Harness : IDisposable
    {
        private readonly List<fakeDbConnection> _armed = new();
        private Action<fakeDbConnection>? _arm;

        public Harness(string mode)
        {
            var singleWriter = mode == SingleWriterSqlite;
            Factory = new fakeDbFactory(singleWriter ? SupportedDatabase.Sqlite : SupportedDatabase.PostgreSql);
            Factory.ConnectionInitializer = connection =>
            {
                var arm = _arm;
                if (arm == null)
                {
                    return;
                }

                lock (_armed)
                {
                    _armed.Add(connection);
                }

                arm(connection);
            };
            Context = new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = singleWriter
                    ? "Data Source=release.db;EmulatedProduct=Sqlite"
                    : "Host=db;Database=test;EmulatedProduct=PostgreSql",
                DbMode = singleWriter ? DbMode.SingleWriter : DbMode.Standard
            }, Factory);
        }

        public fakeDbFactory Factory { get; }
        public DatabaseContext Context { get; }

        public void Arm(Action<fakeDbConnection> arm) => _arm = arm;

        public void AssertEverythingReleased()
        {
            Assert.Equal(0, Context.GetPoolStatisticsSnapshot(PoolLabel.Reader).InUse);
            Assert.Equal(0, Context.GetPoolStatisticsSnapshot(PoolLabel.Writer).InUse);
            Assert.Equal(0, Context.NumberOfOpenConnections);
            lock (_armed)
            {
                Assert.NotEmpty(_armed);
                foreach (var connection in _armed)
                {
                    Assert.True(connection.DisposeCount > 0, "a provider connection was never disposed");
                }
            }
        }

        public void Dispose() => Context.Dispose();
    }

    private static List<Dictionary<string, object>> Rows(int count)
    {
        var rows = new List<Dictionary<string, object>>();
        for (var i = 1; i <= count; i++)
        {
            rows.Add(new Dictionary<string, object> { ["id"] = i, ["name"] = "n" + i });
        }

        return rows;
    }

    private static fakeDbDataReader FailingReader(int rowsBeforeFailure) =>
        new(Rows(rowsBeforeFailure + 1))
        {
            FailAfterReadCount = rowsBeforeFailure,
            FailException = new InvalidOperationException("connection dropped mid-read")
        };

    // ── Plain commands ────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task OpenFails_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(c => c.SetFailOnOpen());

        await using var sc = h.Context.CreateSqlContainer("UPDATE items SET name = 'x'");
        await Assert.ThrowsAnyAsync<Exception>(async () => await sc.ExecuteNonQueryAsync());

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task ExecuteNonQuery_CommandThrows_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(c => c.SetNonQueryExecuteException(new InvalidOperationException("statement failed")));

        await using var sc = h.Context.CreateSqlContainer("UPDATE items SET name = 'x'");
        await Assert.ThrowsAnyAsync<Exception>(async () => await sc.ExecuteNonQueryAsync());

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task ExecuteScalar_CommandThrows_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(c => c.SetScalarExecuteException(new InvalidOperationException("statement failed")));

        await using var sc = h.Context.CreateSqlContainer("SELECT count(*) FROM items");
        await Assert.ThrowsAnyAsync<Exception>(async () => await sc.ExecuteScalarRequiredAsync<long>());

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task ExecuteReader_CommandThrows_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(c => c.SetFailOnCommand());

        await using var sc = h.Context.CreateSqlContainer("SELECT id, name FROM items");
        await Assert.ThrowsAnyAsync<Exception>(async () => await sc.ExecuteReaderAsync());

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task ProviderCloseThrows_AfterASuccessfulCommand_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(c => c.SetFailOnClose(new InvalidOperationException("close failed")));

        await using (var sc = h.Context.CreateSqlContainer("UPDATE items SET name = 'x'"))
        {
            await sc.ExecuteNonQueryAsync();
        }

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task ProviderDisposeThrows_AfterASuccessfulCommand_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(c => c.SetFailOnDispose(new InvalidOperationException("dispose failed")));

        await using (var sc = h.Context.CreateSqlContainer("UPDATE items SET name = 'x'"))
        {
            await sc.ExecuteNonQueryAsync();
        }

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task PreCancelledToken_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(_ => { });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await using var sc = h.Context.CreateSqlContainer("UPDATE items SET name = 'x'");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await sc.ExecuteNonQueryAsync(CommandType.Text, cts.Token));

        Assert.Equal(0, h.Context.GetPoolStatisticsSnapshot(PoolLabel.Writer).InUse);
        Assert.Equal(0, h.Context.NumberOfOpenConnections);
    }

    // ── Readers ───────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Reader_ReadThrowsMidStream_ReleasesTheSlotOnDispose(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(c => c.EnqueueReaderResult(FailingReader(1)));

        await using var sc = h.Context.CreateSqlContainer("SELECT id, name FROM items");
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var reader = await sc.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
            }
        });

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Reader_CloseThrowsOnDisposeAsync_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(c => c.EnqueueReaderResult(new fakeDbDataReader(Rows(2))
        {
            FailOnClose = new InvalidOperationException("drain failed")
        }));

        await using var sc = h.Context.CreateSqlContainer("SELECT id, name FROM items");
        var reader = await sc.ExecuteReaderAsync();
        await reader.ReadAsync();
        try
        {
            await reader.DisposeAsync();
        }
        catch (InvalidOperationException)
        {
            // Whether the close failure surfaces is not the guarantee; the release is.
        }

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Reader_CloseThrowsOnDispose_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(c => c.EnqueueReaderResult(new fakeDbDataReader(Rows(2))
        {
            FailOnClose = new InvalidOperationException("drain failed")
        }));

        await using var sc = h.Context.CreateSqlContainer("SELECT id, name FROM items");
        var reader = await sc.ExecuteReaderAsync();
        reader.Read();
        try
        {
            reader.Dispose();
        }
        catch (InvalidOperationException)
        {
            // Whether the close failure surfaces is not the guarantee; the release is.
        }

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Gateway_LoadList_ReadThrowsMidStream_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        var gateway = new TableGateway<ReleaseItem, int>(h.Context);
        h.Arm(c => c.EnqueueReaderResult(FailingReader(1)));

        await using var sc = gateway.BuildBaseRetrieve("a");
        await Assert.ThrowsAnyAsync<Exception>(async () => await gateway.LoadListAsync(sc));

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Gateway_LoadStream_AbandonedAfterTheFirstRow_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        var gateway = new TableGateway<ReleaseItem, int>(h.Context);
        h.Arm(c => c.EnqueueReaderResult(new fakeDbDataReader(Rows(5))));

        await using var sc = gateway.BuildBaseRetrieve("a");
        await foreach (var _ in gateway.LoadStreamAsync(sc))
        {
            break;
        }

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Gateway_LoadStream_ReadThrowsMidStream_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        var gateway = new TableGateway<ReleaseItem, int>(h.Context);
        h.Arm(c => c.EnqueueReaderResult(FailingReader(1)));

        await using var sc = gateway.BuildBaseRetrieve("a");
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await foreach (var _ in gateway.LoadStreamAsync(sc))
            {
            }
        });

        h.AssertEverythingReleased();
    }

    // ── Transactions ──────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Modes))]
    public void Transaction_BeginFails_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(c => c.SetFailOnBeginTransaction());

        Assert.ThrowsAny<Exception>(() => h.Context.BeginTransaction());

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Transaction_CommitThrows_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(c => c.SetTransactionCommitException(new InvalidOperationException("commit failed")));

        using (var tx = h.Context.BeginTransaction())
        {
            Assert.ThrowsAny<Exception>(() => tx.Commit());
        }

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Transaction_RollbackThrows_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(c => c.SetTransactionRollbackException(new InvalidOperationException("rollback failed")));

        using (var tx = h.Context.BeginTransaction())
        {
            Assert.ThrowsAny<Exception>(() => tx.Rollback());
        }

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Transaction_DisposedWithoutCompleting_RollbackThrows_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(c => c.SetTransactionRollbackException(new InvalidOperationException("rollback failed")));

        var tx = h.Context.BeginTransaction();
        try
        {
            tx.Dispose();
        }
        catch (Exception)
        {
            // Whether the rollback failure surfaces is not the guarantee; the release is.
        }

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Transaction_CommandThrowsInside_ThenDispose_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(c => c.SetNonQueryExecuteException(new InvalidOperationException("statement failed")));

        await using (var tx = await h.Context.BeginTransactionAsync())
        {
            await using var sc = tx.CreateSqlContainer("UPDATE items SET name = 'x'");
            await Assert.ThrowsAnyAsync<Exception>(async () => await sc.ExecuteNonQueryAsync());
        }

        h.AssertEverythingReleased();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Transaction_ProviderCloseThrowsAfterCommit_ReleasesTheSlot(string mode)
    {
        using var h = new Harness(mode);
        h.Arm(c => c.SetFailOnClose(new InvalidOperationException("close failed")));

        using (var tx = h.Context.BeginTransaction())
        {
            tx.Commit();
        }

        h.AssertEverythingReleased();
    }
}
