using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Source-tree claims of the external audit of 2.0.6 (G-03, review policy 4B), pinned so a change is deliberate.
/// </summary>
public class LibrarySourceAuditTests
{
    private static IEnumerable<string> LibrarySources()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var library = Path.Combine(root, "pengdows.crud");
        return Directory.EnumerateFiles(library, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    // G-03: no finalizer or GC backstop anywhere in the library, so an undisposed connection, reader or transaction keeps
    // its governor permit. If a backstop is added, TrackedConnectionSlotLifetimeTests.AbandonedConnection_* must change on
    // purpose too, and docs/core-invariants.md must say so.
    [Fact]
    public void G03_TheLibraryDeclaresNoFinalizer()
    {
        var finalizer = new Regex(@"^\s*(?:public |internal |protected |private )?~[A-Z]\w*\s*\(\s*\)", RegexOptions.Multiline);

        var offenders = LibrarySources().Where(f => finalizer.IsMatch(File.ReadAllText(f))).ToList();

        Assert.True(offenders.Count == 0, "Finalizers: " + string.Join(", ", offenders));
    }

    // Review policy 4B: a swallowed failure is a security defect. The audit's claim, verified: no one-line empty catch
    // ("catch { }") in the library. Catch blocks whose body is only a comment do exist (about thirty, mostly cleanup that must
    // not throw); whether each is justified is a review decision, so they are not pinned here.
    [Fact]
    public void Policy4B_NoOneLineEmptyCatch()
    {
        var oneLine = new Regex(@"catch\s*(?:\([^)]*\))?\s*\{[ \t]*\}");
        var offenders = new List<string>();
        foreach (var file in LibrarySources())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (oneLine.IsMatch(lines[i]))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "One-line empty catches: " + string.Join(", ", offenders));
    }

    // CATCH-001: a catch whose whole body is a comment (or nothing) must say why swallowing is correct: at least five words,
    // so "ignore" and "/* ignore */" fail and the list cannot grow back.
    [Fact]
    public void Catch001_EveryCommentOnlyCatchStatesAReason()
    {
        var catchOpen = new Regex(@"^\s*catch\b[^{]*$");
        var offenders = new List<string>();
        foreach (var file in LibrarySources())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i + 2 < lines.Length; i++)
            {
                if (!catchOpen.IsMatch(lines[i]) || lines[i + 1].Trim() != "{")
                {
                    continue;
                }

                var body = new List<string>();
                var j = i + 2;
                while (j < lines.Length && lines[j].Trim() != "}")
                {
                    body.Add(lines[j].Trim());
                    j++;
                }

                var code = body.Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal) && !l.StartsWith("/*", StringComparison.Ordinal)).ToList();
                if (code.Count > 0)
                {
                    continue;
                }

                var words = string.Join(" ", body).Replace("//", " ").Replace("/*", " ").Replace("*/", " ")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                if (words < 5)
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Catch blocks with no stated reason: " + string.Join(", ", offenders));
    }

    // Review policy 4F: braces are required on every control-flow block, and one statement per line (`if (x) return;` hides the
    // return from a diff). Multi-line conditions are not scanned, only the one-line form and an unbraced else.
    [Fact]
    public void Policy4F_NoUnbracedControlFlow()
    {
        var oneLine = new Regex(@"^\s*(?:if|else\s+if|while|for|foreach)\s*\(.*\)\s*[^{\s/].*;\s*(?://.*)?$");
        var bareElse = new Regex(@"^\s*else\s+[^{i\s/][^{]*;\s*(?://.*)?$");
        var offenders = new List<string>();
        foreach (var file in LibrarySources())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (oneLine.IsMatch(lines[i]) || bareElse.IsMatch(lines[i]))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Unbraced control flow: " + string.Join(", ", offenders));
    }
}
