// =============================================================================
// FILE: PoolLabel.cs
// PURPOSE: Enum identifying connection pool type (Reader vs Writer).
//
// AI SUMMARY:
// - Simple enum for labeling connection pools.
// - Reader: Pool for read-only connections.
// - Writer: Pool for write-capable connections.
// - Used by PoolGovernor and PoolSaturatedException for diagnostics.
// - Enables separate pool limits for read and write operations.
// =============================================================================

namespace pengdows.crud.infrastructure;

public enum PoolLabel
{
    Reader,
    Writer
}