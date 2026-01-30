// =============================================================================
// FILE: EventIds.cs
// PURPOSE: Structured logging event IDs for pengdows.crud diagnostics.
//
// AI SUMMARY:
// - Static class containing EventId constants for structured logging.
// - Event ID ranges by category:
//   * 1xxx: Connection mode events (mismatch, coercion)
//   * 2xxx: Metrics events (collection issues)
//   * 3xxx: Dialect/detection events
//   * 4xxx: Connection lifecycle events
// - Defined events:
//   * ModeMismatch (1001): Connection mode suboptimal for database type
//   * ModeCoerced (1002): Mode auto-changed for correctness/safety
//   * MetricsIssue (2001): Non-fatal metrics collection problem
//   * DialectDetection (3001): Database detection/dialect initialization issue
//   * ConnectionLifecycle (4001): Pool/lifecycle informational event
// - All EventIds have descriptive names for log filtering.
// - Used with ILogger.Log() for structured event logging.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace pengdows.crud.diagnostics;

/// <summary>
/// Structured event IDs for logging and diagnostics.
/// </summary>
public static class EventIds
{
    /// <summary>
    /// Connection mode does not match database characteristics (performance warning).
    /// </summary>
    /// <remarks>
    /// Logged when a user explicitly selects a connection mode that is technically safe
    /// but suboptimal for the database type. Examples:
    /// <list type="bullet">
    ///   <item>SingleConnection mode with client-server databases (SQL Server, PostgreSQL)</item>
    ///   <item>SingleWriter mode with databases that support full concurrency</item>
    ///   <item>Standard mode with file-based SQLite (without WAL) causing lock contention</item>
    /// </list>
    /// </remarks>
    public static readonly EventId ModeMismatch = new(1001, "ConnectionModeMismatch");

    /// <summary>
    /// Connection mode was automatically coerced to a different mode for correctness or safety.
    /// </summary>
    /// <remarks>
    /// Logged when the requested mode is incompatible with the database or would cause
    /// correctness issues. Examples:
    /// <list type="bullet">
    ///   <item>Standard mode coerced to SingleConnection for SQLite :memory:</item>
    ///   <item>Standard mode coerced to SingleWriter for SQLite file databases</item>
    ///   <item>Best mode resolved to optimal mode for database type</item>
    /// </list>
    /// </remarks>
    public static readonly EventId ModeCoerced = new(1002, "ConnectionModeCoerced");

    /// <summary>
    /// Metrics collection has encountered an issue (non-fatal).
    /// </summary>
    public static readonly EventId MetricsIssue = new(2001, "MetricsIssue");

    /// <summary>
    /// Database detection or dialect initialization encountered a non-fatal issue.
    /// </summary>
    public static readonly EventId DialectDetection = new(3001, "DialectDetection");

    /// <summary>
    /// Connection pooling or lifecycle management informational event.
    /// </summary>
    public static readonly EventId ConnectionLifecycle = new(4001, "ConnectionLifecycle");
}