// =============================================================================
// FILE: PoolForbiddenException.cs
// PURPOSE: Exception when a pool with MaxPoolSize=0 is accessed.
//
// AI SUMMARY:
// - Thrown when Acquire/AcquireAsync is called on a forbidden pool (MaxPoolSize=0).
// - Distinct from PoolSaturatedException (pool full) — this pool is explicitly
//   configured to reject all connection requests, e.g. the write pool on a
//   ReadOnly DatabaseContext.
// - Properties:
//   * PoolLabel: Identifies which pool (e.g., Write, Read)
//   * PoolKeyHash: Hashed pool key for correlation
// - Message indicates the pool label and key so operators can diagnose config.
// =============================================================================

using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.exceptions;

public sealed class PoolForbiddenException : InvalidOperationException
{
    public PoolForbiddenException(PoolLabel label, string poolKeyHash)
        : base(
            $"{label} pool is forbidden (MaxPoolSize=0): {label} connections are not permitted with this configuration (key {poolKeyHash}).")
    {
        PoolLabel = label;
        PoolKeyHash = poolKeyHash;
    }

    public PoolLabel PoolLabel { get; }
    public string PoolKeyHash { get; }
}
