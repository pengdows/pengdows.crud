using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

namespace CrudBenchmarks;

internal sealed record SqlServerValidationConfig
{
    public required string BenchmarkFamily { get; init; }
    public required string Variant { get; init; }
    public required string ConnectionString { get; init; }
    public required string Sql { get; init; }
    public required string ViewSchema { get; init; }
    public required string ViewName { get; init; }
    public Func<SqlConnection, Task>? SessionSetup { get; init; }
    public IReadOnlyDictionary<string, string>? RequiredSessionOptions { get; init; }
    public IReadOnlyDictionary<string, string>? ForbiddenSessionOptions { get; init; }
    public IReadOnlyCollection<string>? ProhibitedTableReferences { get; init; }
    public bool ExpectViewReference { get; init; }
    public bool ExpectNoViewReference { get; init; }
    public string? ExpectedViewIndexName { get; init; }
}

internal sealed record SqlServerValidationResult(
    string PlanPath,
    string SessionOptionsPath,
    IReadOnlyDictionary<string, string> SessionOptions,
    string ViewIndexName);

internal static class SqlServerBenchmarkValidation
{
    private const string ValidationRoot = "BenchmarkDotNet.Artifacts";
    // SHOWPLAN XML nests every element inside this namespace.  Descendants("Object")
    // without it silently returns empty and all plan assertions pass vacuously.
    // Do not remove.
    private static readonly XNamespace ShowPlanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    public static async Task<SqlServerValidationResult> ValidateAsync(SqlServerValidationConfig config)
    {
        if (config.ExpectViewReference && config.ExpectNoViewReference)
        {
            throw new ArgumentException("Validation config cannot both expect and forbid view references.");
        }

        await using var connection = new SqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var verifiedIndex = await EnsureViewHasUniqueClusteredIndexAsync(connection, config.ViewSchema, config.ViewName);
        var expectedIndex = config.ExpectedViewIndexName ?? verifiedIndex;

        if (config.SessionSetup != null)
        {
            await config.SessionSetup(connection);
        }

        var planXml = await CaptureExecutionPlanAsync(connection, config.Sql);
        var planDirectory = GetValidationDirectory(config.BenchmarkFamily, config.Variant);
        var planPath = Path.Combine(planDirectory, "plan.xml");
        await File.WriteAllTextAsync(planPath, planXml);

        var planDoc = XDocument.Parse(planXml);
        ValidatePlanObjects(planDoc, config, expectedIndex, planPath);

        var sessionOptions = await CaptureSessionOptionsAsync(connection);
        var optionsPath = Path.Combine(planDirectory, "session-options.txt");
        var serializedOptions = string.Join(Environment.NewLine, sessionOptions.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
        await File.WriteAllTextAsync(optionsPath, serializedOptions);

        ValidateSessionOptions(sessionOptions, config, planPath, optionsPath);

        Console.WriteLine($"[BENCHMARK] Validation pods: {config.BenchmarkFamily}/{config.Variant} plan={planPath} options={optionsPath}");

        return new SqlServerValidationResult(planPath, optionsPath, sessionOptions, expectedIndex);
    }

    private static string GetValidationDirectory(string family, string variant)
    {
        var path = Path.Combine(ValidationRoot, "validation", family, variant);
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> CaptureExecutionPlanAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SET STATISTICS XML ON;\n{sql};\nSET STATISTICS XML OFF;";

        using var reader = await command.ExecuteReaderAsync();
        string? planXml = null;
        do
        {
            if (reader.FieldCount > 0)
            {
                var firstColumn = reader.GetName(0);
                if (firstColumn.Contains("Showplan", StringComparison.OrdinalIgnoreCase))
                {
                    if (await reader.ReadAsync())
                    {
                        planXml = reader.GetString(0);
                        break;
                    }
                }
            }
        } while (await reader.NextResultAsync());

        return planXml ?? throw new InvalidOperationException("Unable to capture SHOWPLAN XML result.");
    }

    private static async Task<IReadOnlyDictionary<string, string>> CaptureSessionOptionsAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DBCC USEROPTIONS";
        using var reader = await command.ExecuteReaderAsync();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            options[reader.GetString(0)] = reader.GetString(1);
        }

        return options;
    }

    private static void ValidateSessionOptions(
        IReadOnlyDictionary<string, string> actual,
        SqlServerValidationConfig config,
        string planPath,
        string optionsPath)
    {
        if (config.RequiredSessionOptions != null)
        {
            foreach (var pair in config.RequiredSessionOptions)
            {
                if (!actual.TryGetValue(pair.Key, out var value) ||
                    !string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase))
                {
                    throw CreateValidationException(
                        $"Session option '{pair.Key}' expected '{pair.Value}' but was '{value ?? "<missing>"}'",
                        planPath,
                        optionsPath);
                }
            }
        }

        if (config.ForbiddenSessionOptions != null)
        {
            foreach (var pair in config.ForbiddenSessionOptions)
            {
                if (actual.TryGetValue(pair.Key, out var value) &&
                    string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase))
                {
                    throw CreateValidationException(
                        $"Session option '{pair.Key}' must not be '{pair.Value}'",
                        planPath,
                        optionsPath);
                }
            }
        }
    }

    /// <summary>
    /// Strip SQL Server bracket quoting from identifiers (e.g. "[dbo]" → "dbo").
    /// SHOWPLAN XML uses bracketed identifiers while config uses unbracketed names.
    /// </summary>
    private static string StripBrackets(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Length >= 2 && value[0] == '[' && value[^1] == ']')
            return value[1..^1];
        return value;
    }

    private static void ValidatePlanObjects(XDocument planDoc, SqlServerValidationConfig config, string expectedIndex, string planPath)
    {
        var objects = planDoc.Descendants(ShowPlanNs + "Object")
            .Select(o => new
            {
                Schema = StripBrackets((string?)o.Attribute("Schema")),
                Table = StripBrackets((string?)o.Attribute("Table")),
                Index = StripBrackets((string?)o.Attribute("Index"))
            })
            .Where(o => !string.IsNullOrEmpty(o.Table))
            .ToList();

        var viewReferenced = objects.Any(o =>
            string.Equals(o.Table, config.ViewName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(o.Schema, config.ViewSchema, StringComparison.OrdinalIgnoreCase));

        if (config.ExpectViewReference && !viewReferenced)
        {
            throw CreateValidationException($"Plan missing indexed view reference for {config.ViewSchema}.{config.ViewName}", planPath);
        }

        if (config.ExpectNoViewReference && viewReferenced)
        {
            throw CreateValidationException($"Plan unexpectedly referenced indexed view {config.ViewSchema}.{config.ViewName}", planPath);
        }

        if (config.ExpectViewReference && !objects.Any(o => string.Equals(o.Index, expectedIndex, StringComparison.OrdinalIgnoreCase)))
        {
            throw CreateValidationException($"Plan did not mention expected index '{expectedIndex}'", planPath);
        }

        if (config.ProhibitedTableReferences != null)
        {
            foreach (var forbidden in config.ProhibitedTableReferences)
            {
                if (objects.Any(o => string.Equals(o.Table, forbidden, StringComparison.OrdinalIgnoreCase)))
                {
                    throw CreateValidationException($"Plan referenced forbidden table '{forbidden}'", planPath);
                }
            }
        }
    }

    private static async Task<string> EnsureViewHasUniqueClusteredIndexAsync(SqlConnection connection, string schema, string view)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT i.name
            FROM sys.indexes i
            JOIN sys.views v ON i.object_id = v.object_id
            WHERE SCHEMA_NAME(v.schema_id) = @schema
              AND v.name = @view
              AND i.type_desc = 'CLUSTERED'
              AND i.is_unique = 1";
        command.Parameters.Add(new SqlParameter("@schema", schema));
        command.Parameters.Add(new SqlParameter("@view", view));

        var indexName = await command.ExecuteScalarAsync() as string;
        if (string.IsNullOrEmpty(indexName))
        {
            throw new InvalidOperationException($"Indexed view {schema}.{view} is missing a UNIQUE CLUSTERED index.");
        }

        return indexName;
    }

    private static InvalidOperationException CreateValidationException(string message, string planPath, string? optionsPath = null)
    {
        var detail = $"{message}. Plan: {planPath}";
        if (!string.IsNullOrEmpty(optionsPath))
        {
            detail += $", Session options: {optionsPath}";
        }

        return new InvalidOperationException(detail);
    }
}
