using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Deprecation policy for the 2.x line: every [Obsolete] in the shipped assemblies is a warning (IsError = false)
/// and its message names the 3.0 removal, so a caller can see when the member goes and what replaces it.
/// This scans every public type and member, so a marker added later is held to the same rule.
/// </summary>
public class ObsoletePolicyTests
{
    [Fact]
    public void EveryObsoleteMarker_IsWarningOnly_AndNamesThe3_0Removal()
    {
        var assemblies = new[]
        {
            typeof(DatabaseContext).Assembly,
            typeof(pengdows.crud.IDatabaseContext).Assembly,
            typeof(pengdows.crud.fakeDb.fakeDbFactory).Assembly,
        };

        var violations = new List<string>();
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes().Where(t => t.IsPublic))
            {
                Check(type.GetCustomAttribute<ObsoleteAttribute>(), type.FullName!, violations);

                var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                                              BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                foreach (var member in members)
                {
                    Check(member.GetCustomAttribute<ObsoleteAttribute>(),
                        $"{type.FullName}.{member.Name}", violations);
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Obsolete markers that break the 2.x deprecation policy:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    private static void Check(ObsoleteAttribute? obsolete, string name, List<string> violations)
    {
        if (obsolete is null)
        {
            return;
        }

        // The compiler emits this error-level marker on types with `required` members so older compilers cannot
        // construct them. It is not a deprecation, so the policy does not apply to it.
        if ((obsolete.Message ?? string.Empty).Contains("required members", StringComparison.Ordinal))
        {
            return;
        }

        if (obsolete.IsError)
        {
            violations.Add($"{name}: IsError is true; a 2.x deprecation is a warning.");
        }

        var message = obsolete.Message ?? string.Empty;
        if (!message.Contains("3.0", StringComparison.Ordinal))
        {
            violations.Add($"{name}: message does not name the 3.0 removal: \"{message}\"");
        }
    }
}
