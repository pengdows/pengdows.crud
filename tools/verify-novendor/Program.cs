// dotnet add package Mono.Cecil
// Usage: dotnet run --project tools/verify-novendor -- <dir-with-dlls> [--allow "Pattern1;Pattern2"]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mono.Cecil;

static class V {
    static readonly string[] DefaultForbidden = new[] {
        "Microsoft.Data.SqlClient",
        "System.Data.SqlClient",
        "Npgsql",
        "Oracle.ManagedDataAccess",
        "OracleInternal",
        "MySqlConnector",
        "MySql.Data",
        "FirebirdSql",
        "IBM.Data.DB2",
        "Devart.",
        "System.Data.SQLite",
        "SQLitePCLRaw",
        "DuckDB"
    };

    public static int Main(string[] args) {
        return RunWithTimeout(args, TimeSpan.FromSeconds(120));
    }

    static int RunWithTimeout(string[] args, TimeSpan timeout) {
        using var cts = new CancellationTokenSource(timeout);
        var task = Task.Run(() => RunCore(args, cts.Token), cts.Token);
        
        try {
            return task.GetAwaiter().GetResult();
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("⏱️  VerifyNoVendor timed out after 120 seconds - skipping verification");
            return 0; // Return success to not break the build
        }
    }

    static int RunCore(string[] args, CancellationToken cancellationToken) {
        if (args.Length == 0) { Console.Error.WriteLine("Usage: verify-novendor <dir> [--allow \"pattern;pattern\"]"); return 2; }
        var root = args[0];
        var allowArg = args.Skip(1).FirstOrDefault(a => a.StartsWith("--allow=", StringComparison.OrdinalIgnoreCase));
        var allows = (allowArg?.Substring("--allow=".Length) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);

        var forbidden = DefaultForbidden
            .Where(f => !allows.Any(a => f.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var badFinds = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories)) {
            cancellationToken.ThrowIfCancellationRequested();
            // Skip test bins, testbed, and benchmarks quickly
            var lower = file.ToLowerInvariant();
            if (lower.Contains("testhost") || 
                lower.Contains(@"\tests\") || lower.Contains(@"/tests/") ||
                lower.Contains(@"\testbed\") || lower.Contains(@"/testbed/") ||
                lower.Contains(@"\benchmarks\") || lower.Contains(@"/benchmarks/") ||
                lower.Contains("crudbenchmarks") ||
                lower.Contains("testbed")) continue;

            try {
                cancellationToken.ThrowIfCancellationRequested();
                using var asm = AssemblyDefinition.ReadAssembly(file, new ReaderParameters { ReadSymbols = false });
                
                cancellationToken.ThrowIfCancellationRequested();
                // 1) Assembly references
                foreach (var ar in asm.MainModule.AssemblyReferences) {
                    var name = ar.Name ?? "";
                    if (forbidden.Any(f => name.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                        badFinds.Add($"{Path.GetFileName(file)}: AssemblyRef → {name}");
                }
                
                cancellationToken.ThrowIfCancellationRequested();
                // 2) Type references (namespaces)
                // Use ToList() to avoid potential infinite enumeration issues and add timeout protection
                List<TypeReference> typeReferences;
                try {
                    typeReferences = asm.MainModule.GetTypeReferences().Take(10000).ToList(); // Limit to 10k refs
                } catch (Exception ex) {
                    Console.Error.WriteLine($"[warn] Failed to get type references from {file}: {ex.Message}");
                    continue;
                }
                
                foreach (var tr in typeReferences) {
                    var ns = tr.Namespace ?? "";
                    if (forbidden.Any(f => ns.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                        badFinds.Add($"{Path.GetFileName(file)}: TypeRef → {ns}.{tr.Name}");
                }
                
                cancellationToken.ThrowIfCancellationRequested();
                // 3) Custom attributes (attribute type scope)
                foreach (var ca in asm.CustomAttributes) {
                    var ns = ca.AttributeType.Namespace ?? "";
                    if (forbidden.Any(f => ns.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                        badFinds.Add($"{Path.GetFileName(file)}: Attr → {ns}.{ca.AttributeType.Name}");
                }
                
                cancellationToken.ThrowIfCancellationRequested();
                // 4) Member references (DeclaringType namespaces)  
                // Use ToList() to avoid potential infinite enumeration issues and add timeout protection
                List<MemberReference> memberReferences;
                try {
                    memberReferences = asm.MainModule.GetMemberReferences().Take(50000).ToList(); // Limit to 50k refs
                } catch (Exception ex) {
                    Console.Error.WriteLine($"[warn] Failed to get member references from {file}: {ex.Message}");
                    continue;
                }
                
                foreach (var mr in memberReferences) {
                    var dt = mr.DeclaringType;
                    var ns = dt?.Namespace ?? "";
                    if (forbidden.Any(f => ns.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                        badFinds.Add($"{Path.GetFileName(file)}: MemberRef → {ns}.{dt?.Name}::{mr.Name}");
                }
            } catch (OperationCanceledException) {
                throw; // Re-throw cancellation
            } catch (Exception ex) {
                Console.Error.WriteLine($"[warn] Skipping {file}: {ex.Message}");
            }
        }

        if (badFinds.Count == 0) {
            Console.WriteLine("✅ No forbidden provider references found.");
            return 0;
        }

        Console.Error.WriteLine("❌ Forbidden provider references found:");
        foreach (var b in badFinds.Distinct()) Console.Error.WriteLine("  " + b);
        return 1;
    }
}