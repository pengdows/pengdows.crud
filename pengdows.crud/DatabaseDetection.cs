using System.Data;

namespace pengdows.crud;

/// <summary>Provides database-product detection with optional diagnostic evidence.</summary>
public static class DatabaseDetection
{
    /// <summary>Detects the product and returns the probes that produced the result.</summary>
    public static DatabaseDetectionResult DetectFromConnectionWithDetail(IDbConnection? connection)
        => @internal.DatabaseDetectionService.DetectFromConnectionWithDetail(connection);

    /// <summary>Asynchronously detects the product and returns its probe evidence.</summary>
    public static Task<DatabaseDetectionResult> DetectFromConnectionWithDetailAsync(
        IDbConnection? connection, CancellationToken cancellationToken = default)
        => @internal.DatabaseDetectionService.DetectFromConnectionWithDetailAsync(connection, cancellationToken);
}
