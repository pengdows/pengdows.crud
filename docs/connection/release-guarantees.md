# Connection release guarantees

pengdows.crud owns every connection it opens. This page states what that ownership guarantees when something fails,
path by path, and names the test that proves each one. Every test named here runs in the unit suite on fakeDb, which
produces the provider failures itself (`SetFailOnOpen`, `SetFailOnClose`, `SetFailOnDispose`, a reader's
`FailException`/`FailOnClose`, transaction commit/rollback/dispose exceptions); nothing is mocked.
`DocumentationConsistencyTests.ReleaseGuarantees_EveryCitedTestExists_AndThePageIsIndexed` fails the build if a test
cited here is renamed or removed.

## The guarantee

For every connection pengdows.crud opens for an operation, whatever the provider throws and wherever it throws it:

1. **The pool slot goes back to the governor.** The reader and writer governors end with nothing in use.
2. **The provider connection is disposed.** Its `Dispose`/`DisposeAsync` runs, so the provider returns it to its own pool.
3. **The open-connection count comes back down.** `NumberOfOpenConnections` and the connection metrics return to
   zero, including when the provider's `Close` throws and never reports the close (fixed 2026-10-19; before that the
   count stayed one too high for good after such a failure).
4. **The original failure is what you see.** A cleanup failure (close, dispose, rollback) is logged, not thrown over
   the exception that caused it. Cancellation propagates as `OperationCanceledException`, unwrapped.

`ConnectionReleaseGuaranteeTests` checks 1–3 together for each failure path below, in `Standard` mode (PostgreSQL)
and in `SingleWriter` mode (file SQLite, the turnstile governor).

## Failure paths

### Opening and executing

| Failure | Test |
|---|---|
| The open fails | `ConnectionReleaseGuaranteeTests.OpenFails_ReleasesTheSlot` |
| `ExecuteNonQueryAsync` throws | `ConnectionReleaseGuaranteeTests.ExecuteNonQuery_CommandThrows_ReleasesTheSlot` |
| `ExecuteScalar…Async` throws | `ConnectionReleaseGuaranteeTests.ExecuteScalar_CommandThrows_ReleasesTheSlot` |
| `ExecuteReaderAsync` throws (read or write intent) | `ConnectionReleaseGuaranteeTests.ExecuteReader_CommandThrows_ReleasesTheSlot`, `ExecuteReaderWriteConnectionLeakTests.ExecuteReaderAsync_Write_FailureDisposesConnection`, `ExecuteReaderWriteConnectionLeakTests.ExecuteReaderAsync_Read_FailureDisposesConnection` |
| The command is disposed on an execute failure | `SqlContainerCommandDisposalTests.ExecuteReaderAsync_DisposesCommandOnExecuteFailure` |
| The provider's `Close` throws after a successful command | `ConnectionReleaseGuaranteeTests.ProviderCloseThrows_AfterASuccessfulCommand_ReleasesTheSlot`, `TrackedWrapperTests.TrackedConnection_Dispose_ReleasesSlot_WhenUnderlyingDisposeThrows` |
| The provider's `Dispose` throws | `ConnectionReleaseGuaranteeTests.ProviderDisposeThrows_AfterASuccessfulCommand_ReleasesTheSlot`, `TrackedConnectionAdditionalBranchTests.DisposeAsync_WhenUnderlyingDisposeAsyncThrows_FallsBackToSync` |
| The provider refuses `StateChange` access once disposed (Sybase ASE) | `DisposedConnectionRefusesStateChangeAccessTests.Commands_DisposeTheirConnections_AndReleaseTheirPoolSlots` |
| The provider never raises `StateChange` (Snowflake) | `SilentStateChangeDriverConnectionCountTests.Transaction_CountsItsConnectionOnce_WhetherOrNotTheDriverReportsIt` |

### Cancellation

| Failure | Test |
|---|---|
| The token is already cancelled | `ConnectionReleaseGuaranteeTests.PreCancelledToken_ReleasesTheSlot` |
| Cancelled while waiting for a slot | `PoolGovernorCancellationTests.AcquireAsync_CanceledWhileQueued_IncrementsTotalCanceledWaits`, `PoolGovernorCancellationTests.Acquire_CanceledWhileQueued_IncrementsTotalCanceledWaits` |
| Cancelled while the command runs | `CancellationLeakTests.ExecuteNonQueryAsync_WhenCanceled_DoesNotLeakPoolSlot`, `CancellationMidFlightMatrixTests.CancelledWhileInFlight_ThrowsOperationCanceled_AndNeverReachesFakeExecution` |
| Cancelled while reading | `CancellationLeakTests.ExecuteReaderAsync_WhenCanceledDuringRead_DoesNotLeakPoolSlot`, `CancellationLeakTests.RetrieveStreamAsync_WhenCanceledDuringIteration_DoesNotLeakPoolSlot` |
| A cancelled write still rolls back | `FailedWriteRollbackCancellationTests.ExecuteNonQueryAsync_CancelledWrite_StillIssuesRollback`, `FailedWriteRollbackCancellationTests.ExecuteReaderAsync_CancelledWrite_StillIssuesRollback` |
| Cancelled while the transaction begins | `TransactionBeginCancellationTests.BeginTransactionAsync_CancelledDuringBegin_PropagatesUnwrapped` |

### Readers and streams

A reader is a lease on its connection: the connection is released when the reader is disposed, not before.

| Failure | Test |
|---|---|
| A read throws partway through the results | `ConnectionReleaseGuaranteeTests.Reader_ReadThrowsMidStream_ReleasesTheSlotOnDispose` |
| The reader's own close throws (`Dispose`/`DisposeAsync`) | `ConnectionReleaseGuaranteeTests.Reader_CloseThrowsOnDispose_ReleasesTheSlot`, `ConnectionReleaseGuaranteeTests.Reader_CloseThrowsOnDisposeAsync_ReleasesTheSlot` |
| The command is disposed with the reader | `SqlContainerCommandDisposalTests.ExecuteReaderAsync_DisposesCommandAfterReaderDisposed` |
| `LoadListAsync` hits a failing read | `ConnectionReleaseGuaranteeTests.Gateway_LoadList_ReadThrowsMidStream_ReleasesTheSlot` |
| `LoadStreamAsync` is abandoned after the first row (`break`) | `ConnectionReleaseGuaranteeTests.Gateway_LoadStream_AbandonedAfterTheFirstRow_ReleasesTheSlot` |
| `LoadStreamAsync` hits a failing read | `ConnectionReleaseGuaranteeTests.Gateway_LoadStream_ReadThrowsMidStream_ReleasesTheSlot` |

### Transactions

A transaction pins its connection until it completes or is disposed.

| Failure | Test |
|---|---|
| The open fails during `BeginTransaction` | `TransactionBeginFailureLeakTests.BeginTransaction_OpenFails_ReleasesTheConnection`, `TransactionBeginFailureLeakTests.BeginTransactionAsync_OpenFails_ReleasesTheConnection`, `TransactionOpenFailureTests.BeginTransaction_Sync_WhenTheOpenFailsForAnotherReason_ReleasesTheConnection` |
| The provider's begin fails | `ConnectionReleaseGuaranteeTests.Transaction_BeginFails_ReleasesTheSlot` |
| `Commit` throws | `ConnectionReleaseGuaranteeTests.Transaction_CommitThrows_ReleasesTheSlot`, `TransactionExceptionWiringTests.AfterCommitFailure_TransactionContext_IsCompleted` |
| `Rollback` throws | `ConnectionReleaseGuaranteeTests.Transaction_RollbackThrows_ReleasesTheSlot` |
| Disposed without completing, and the implicit rollback throws | `ConnectionReleaseGuaranteeTests.Transaction_DisposedWithoutCompleting_RollbackThrows_ReleasesTheSlot` |
| A command inside the transaction throws | `ConnectionReleaseGuaranteeTests.Transaction_CommandThrowsInside_ThenDispose_ReleasesTheSlot` |
| The provider transaction's `Dispose` throws | `TransactionCompletionCleanupTests.SyncCompletion_ThrowingTransactionDispose_StillReleasesTheConnection`, `TransactionCompletionCleanupTests.AsyncCompletion_ThrowingTransactionDispose_StillReleasesTheConnection` |
| The provider's `Close` throws after commit | `ConnectionReleaseGuaranteeTests.Transaction_ProviderCloseThrowsAfterCommit_ReleasesTheSlot` |
| `Dispose` races `Commit`/`Rollback` | `TransactionContextDisposeRaceTests.Commit_ConcurrentDispose_TransactionNotDisposedUntilCommitCompletes`, `TransactionContextDisposeRaceTests.RollbackAsync_ConcurrentDisposeAsync_TransactionNotDisposedUntilRollbackCompletes` |

### The governor and the context

| Failure | Test |
|---|---|
| The governor closes between the admission check and the acquire | `PoolGovernorReleaseOrderingTests.OnAcquired_WhenGovernorClosedBetweenCheckAndAcquire_GivesBackPermitAndThrows` |
| A slot is released in the order admission depends on | `PoolGovernorReleaseOrderingTests.ReleaseToken_ReleasesAdmissionBeforeDecrementingInUse` |
| The context is disposed while a lease is outstanding | `DatabaseContextGovernorDisposalTests.DisposeAsync_WaitsForOutstandingLease_ThenDisposesGovernors` |
| An acquire races the context's disposal | `DatabaseContextGovernorDisposalTests.DisposeAsync_RacingWithNewAcquisitionAttempt_RejectsIt_NoPostCloseLeaseGranted`, `DatabaseContextGovernorDisposalTests.DisposeAsync_RacingConnectionStuckMidOpenAsync_DrainsAndSucceedsAfterGateReleased` |
| A data source or persistent connection throws on dispose | `BestEffortDisposalLoggingTests.Dispose_DataSourceDisposeFailure_IsLoggedNotThrown`, `BestEffortDisposalLoggingTests.Dispose_PersistentConnectionDisposeFailure_IsLoggedNotThrown` |
| The context is disposed during a sentinel repair | `SentinelRepairDisposalRaceTests.AsyncRepair_ContextDisposedMidRepair_DoesNotThrowFromTheRepairLock` |
| A shared connection's lock is held past the dispose timeout | `TrackedConnectionAdditionalBranchTests.DisposeAsync_SharedConnection_LockReleasedAfterGiveUp_ConnectionIsStillDisposed` |

## What is not guaranteed

- **A lease you never dispose.** A reader (`ITrackedReader`) or a transaction holds its connection until it is
  disposed. pengdows.crud has no finalizers and does not rely on garbage collection to give a connection back: dispose
  readers and transactions (`await using`).
- **A shared connection, by design.** In `SingleConnection` mode the one connection lives as long as the context; in
  `PreventDatabaseUnload` mode the sentinel connections do. They are released when the context is disposed.
- **A context disposal whose drain times out.** If outstanding leases do not finish within the drain timeout, the
  context does not dispose the data sources it owns underneath them
  (`DatabaseContextGovernorDisposalTests.Dispose_GovernorDrainTimesOut_DoesNotDisposeOwnedDataSources`): it prefers
  leaving them to the provider over breaking connections still in use.
- **What a provider does after `Dispose`.** pengdows.crud guarantees it calls `Dispose`; a provider that leaks a
  physical connection inside its own pool after that is outside the library's reach. The ADO.NET conformance suite
  (`pengdows.AdoNetApiTest.conformance`) tests providers for that separately.
