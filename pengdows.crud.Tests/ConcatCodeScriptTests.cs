using System;
using System.IO;
using System.Linq;
using Xunit;

namespace pengdows.crud.Tests;

public class ConcatCodeScriptTests
{
    [Fact]
    public void ConcatCodeScript_RestrictsExtensionsToCsAndXml()
    {
        var scriptPath = GetScriptPath();
        var lines = File.ReadAllLines(scriptPath);
        var line = lines.FirstOrDefault(value => value.TrimStart().StartsWith("declare -a code_exts=", StringComparison.Ordinal));

        Assert.False(string.IsNullOrWhiteSpace(line), "Expected concat-code.sh to declare code_exts.");

        var openParen = line!.IndexOf('(');
        var closeParen = line.LastIndexOf(')');

        Assert.True(openParen >= 0, "Expected code_exts to use '(' to open the list.");
        Assert.True(closeParen > openParen, "Expected code_exts to use ')' to close the list.");

        var payload = line.Substring(openParen + 1, closeParen - openParen - 1);
        var extensions = payload.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(6, extensions.Length);
        Assert.Contains("cs", extensions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("sql", extensions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("csproj", extensions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("props", extensions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("targets", extensions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("config", extensions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConcatCodeScript_LimitsSourceDirsToCoreProjects()
    {
        var scriptPath = GetScriptPath();
        var lines = File.ReadAllLines(scriptPath);
        var sourceDirs = GetBashArrayEntries(lines, "source_dirs");

        Assert.Equal(3, sourceDirs.Length);
        Assert.Contains("pengdows.crud", sourceDirs, StringComparer.Ordinal);
        Assert.Contains("pengdows.crud.abstractions", sourceDirs, StringComparer.Ordinal);
        Assert.Contains("testbed", sourceDirs, StringComparer.Ordinal);
    }

    private static string GetScriptPath()
    {
        var root = GetRepoRoot();
        return Path.Combine(root, "concat-code.sh");
    }

    private static string GetRepoRoot()
    {
        var start = new DirectoryInfo(AppContext.BaseDirectory);
        for (var current = start; current != null; current = current.Parent)
        {
            var slnPath = Path.Combine(current.FullName, "pengdows.crud.sln");
            if (File.Exists(slnPath))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root for concat-code.sh validation.");
    }

    private static string[] GetBashArrayEntries(string[] lines, string arrayName)
    {
        var startToken = $"declare -a {arrayName}=";
        var values = new System.Collections.Generic.List<string>();
        var inList = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!inList)
            {
                if (!trimmed.StartsWith(startToken, StringComparison.Ordinal))
                {
                    continue;
                }

                inList = true;
                var remainder = trimmed.Substring(startToken.Length).TrimStart();
                if (!remainder.StartsWith("(", StringComparison.Ordinal))
                {
                    continue;
                }

                remainder = remainder.Substring(1);
                if (remainder.Length > 0)
                {
                    ParseBashListLine(remainder, values, out var closed);
                    if (closed)
                    {
                        break;
                    }
                }

                continue;
            }

            ParseBashListLine(trimmed, values, out var done);
            if (done)
            {
                break;
            }
        }

        return values.ToArray();
    }

    private static void ParseBashListLine(string line, System.Collections.Generic.List<string> values, out bool closed)
    {
        closed = false;
        var working = line;
        var closeIndex = working.IndexOf(')');
        if (closeIndex >= 0)
        {
            working = working.Substring(0, closeIndex);
            closed = true;
        }

        if (string.IsNullOrWhiteSpace(working))
        {
            return;
        }

        var tokens = working.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var value = token.Trim();
            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                break;
            }

            value = value.Trim('"');
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }
    }
}
