// =============================================================================
// FILE: ProviderVersionRequirements.cs
// PURPOSE: Refuses ADO.NET driver versions with known defects pengdows.crud does not work around.
//
// AI SUMMARY:
// - MySqlConnector 2.3.0 through 2.6.0: on MariaDB, executing a prepared INSERT ... RETURNING a second
//   time on a connection fails ("Expected result set to have 0 columns, but it contains 1 columns") and
//   the driver closes the connection, whatever the parameter values. pengdows.crud prepares statements on
//   MySqlConnector, so every repeated insert hits it. 2.6.1 fixed it (confirmed live on MariaDB 11.4.13,
//   2026-10-22); the minimum is 2.6.1.
// - pengdows.flatfile before 0.2.1-preview.6 cannot filter a DATE or TIME column: it refuses
//   DateOnly/TimeOnly parameters and cannot compare a DateTime/TimeSpan with them
//   (pengdows/pengdows.flatfile#1, confirmed live 2026-10-23).
// - The version compared is the informational version without its "+commit" suffix, with SemVer
//   prerelease ordering (every MySqlConnector 2.x reports assembly version 2.0.0.0 and every flatfile
//   0.2.1 preview file version 0.2.1.0), else the file version.
// - MySql.Data is refused at every version (decision 2026-10-22): it fails the ADO.NET contract
//   pengdows.crud relies on (StateChange reports Open -> Open, reader access before Read does not
//   throw, parameter defaults), and the fixes (mysql/mysql-connector-net #95-#115) are not released.
//   Its workarounds stay in the dialects, so allowing it again is removing its row below.
// - Checked by DatabaseContext before it opens any connection. A driver with no version passes the
//   minimums (there is nothing to compare) but not the refusals.
// =============================================================================

using System.Data.Common;
using System.Globalization;
using System.Reflection;

namespace pengdows.crud.@internal;

internal static class ProviderVersionRequirements
{
    private static readonly (string Assembly, string Reason)[] Refused =
    {
        ("MySql.Data",
            "it fails the ADO.NET contract pengdows.crud relies on, such as StateChange and reader state; use MySqlConnector 2.6.1 or later")
    };

    private static readonly (string Assembly, string Minimum, string Reason)[] Minimums =
    {
        ("MySqlConnector", "2.6.1",
            "2.3.0 through 2.6.0 fail a prepared INSERT ... RETURNING run a second time on MariaDB and close the connection"),
        ("pengdows.flatfile", "0.2.1-preview.6",
            "earlier releases refuse DateOnly/TimeOnly parameters and cannot compare a DateTime or TimeSpan with a DATE or TIME column")
    };

    public static void EnsureSupported(DbProviderFactory factory)
    {
        var assembly = factory.GetType().Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational != null && SemanticVersion.TryParse(informational, out _)
            ? informational
            : assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        EnsureSupported(assembly.GetName().Name, version);
    }

    public static void EnsureSupported(string? assemblyName, Version? fileVersion) =>
        EnsureSupported(assemblyName, fileVersion?.ToString());

    public static void EnsureSupported(string? assemblyName, string? version)
    {
        foreach (var (name, reason) in Refused)
        {
            if (string.Equals(assemblyName, name, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"{name} is not supported by pengdows.crud: {reason}.");
            }
        }

        if (version == null || !SemanticVersion.TryParse(version, out var actual))
        {
            return;
        }

        foreach (var (name, minimum, reason) in Minimums)
        {
            if (string.Equals(assemblyName, name, StringComparison.OrdinalIgnoreCase)
                && SemanticVersion.TryParse(minimum, out var required) && actual.CompareTo(required) < 0)
            {
                throw new NotSupportedException(
                    $"{name} {version} is not supported by pengdows.crud; {minimum} or later is required ({reason}).");
            }
        }
    }

    /// <summary>
    /// A version as NuGet writes it: up to four numeric parts, then an optional "-prerelease" and "+build". A release
    /// sorts after its prereleases, and prerelease identifiers compare numerically when both are numbers (SemVer 2.0).
    /// </summary>
    private readonly struct SemanticVersion : IComparable<SemanticVersion>
    {
        private readonly int[] _numbers;
        private readonly string[] _prerelease;

        private SemanticVersion(int[] numbers, string[] prerelease)
        {
            _numbers = numbers;
            _prerelease = prerelease;
        }

        public static bool TryParse(string text, out SemanticVersion version)
        {
            version = default;
            var core = text.Split('+')[0];
            var dash = core.IndexOf('-');
            var numbers = (dash < 0 ? core : core[..dash]).Split('.');
            if (numbers.Length is < 1 or > 4)
            {
                return false;
            }

            var parsed = new int[4];
            for (var i = 0; i < numbers.Length; i++)
            {
                if (!int.TryParse(numbers[i], NumberStyles.None, CultureInfo.InvariantCulture, out parsed[i]))
                {
                    return false;
                }
            }

            version = new SemanticVersion(parsed, dash < 0 ? Array.Empty<string>() : core[(dash + 1)..].Split('.'));
            return true;
        }

        public int CompareTo(SemanticVersion other)
        {
            for (var i = 0; i < 4; i++)
            {
                var byNumber = _numbers[i].CompareTo(other._numbers[i]);
                if (byNumber != 0)
                {
                    return byNumber;
                }
            }

            if (_prerelease.Length == 0 || other._prerelease.Length == 0)
            {
                return other._prerelease.Length.CompareTo(_prerelease.Length);
            }

            for (var i = 0; i < Math.Min(_prerelease.Length, other._prerelease.Length); i++)
            {
                var byIdentifier = CompareIdentifiers(_prerelease[i], other._prerelease[i]);
                if (byIdentifier != 0)
                {
                    return byIdentifier;
                }
            }

            return _prerelease.Length.CompareTo(other._prerelease.Length);
        }

        // SemVer 11.4: numeric identifiers compare numerically (no leading zeros, so by length, then by
        // digits), alphanumeric ones in ASCII order, and a numeric identifier is always the lower.
        private static int CompareIdentifiers(string left, string right)
        {
            var leftNumeric = IsNumeric(left);
            var rightNumeric = IsNumeric(right);
            if (leftNumeric && rightNumeric)
            {
                var byLength = left.Length.CompareTo(right.Length);
                return byLength != 0 ? byLength : string.CompareOrdinal(left, right);
            }

            if (leftNumeric != rightNumeric)
            {
                return leftNumeric ? -1 : 1;
            }

            return string.CompareOrdinal(left, right);
        }

        private static bool IsNumeric(string identifier) =>
            identifier.Length > 0 && identifier.All(char.IsAsciiDigit);
    }
}
