using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// ADO.NET provider behaviour is never mocked: connections, commands, readers, transactions, parameters, factories and data
/// sources are fakeDb's job, and anything fakeDb cannot do is added to fakeDb (it should produce everything a real provider
/// can, and every failure). Mocking anything else, such as the library's own interfaces, is fine and may use Moq. This fails
/// on a mocking framework (or a DispatchProxy) creating one of the provider types below.
/// </summary>
public class NoMockedProviderTypesTests
{
    private static readonly string[] ExcludedFolders =
        { "bin", "obj", ".git", "TestResults", "artifacts", "BenchmarkDotNet.Artifacts", "node_modules" };

    private const string ProviderTypes =
        "(?:System\\.Data\\.(?:Common\\.)?)?(?:DbConnection|DbCommand|DbDataReader|DbTransaction|DbProviderFactory|DbParameter|" +
        "DbParameterCollection|DbDataSource|DbDataAdapter|DbBatch|DbBatchCommand|DbConnectionStringBuilder|IDbConnection|" +
        "IDbCommand|IDataReader|IDataRecord|IDbTransaction|IDataParameter|IDbDataParameter|IDataParameterCollection)";

    // What mocking a provider type looks like in source. Words in comments are not matched.
    private static readonly Regex Forbidden = new(
        @"(?:\bnew\s+(?:Moq\.)?Mock|\bMock\.Of|\bSubstitute\.For|\bA\.Fake|\bDispatchProxy\.Create)\s*<\s*" + ProviderTypes + @"\b",
        RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "pengdows.crud.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("pengdows.crud.sln not found above the test output");
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => string.Equals(Path.GetExtension(f), ".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => ExcludedFolders.Contains(part)))
            // This file names the patterns it forbids.
            .Where(f => !string.Equals(Path.GetFileName(f), nameof(NoMockedProviderTypesTests) + ".cs", StringComparison.Ordinal));

    [Fact]
    public void NoTestMocksAnAdoNetProviderType()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        foreach (var file in SourceFiles(root))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (Forbidden.IsMatch(lines[i]))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "An ADO.NET provider type is mocked (use fakeDb; add what fakeDb lacks to fakeDb):\n" +
            string.Join("\n", offenders));
    }
}
