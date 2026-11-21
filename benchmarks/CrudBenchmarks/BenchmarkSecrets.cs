using System.Security.Cryptography;

namespace CrudBenchmarks;

/// <summary>
/// Passwords for the throwaway database containers the benchmarks start. Each call draws fresh bytes from
/// <see cref="RandomNumberGenerator"/>, so no password is fixed in source and none is reused across runs.
/// </summary>
/// <remarks>
/// The output is 128 bits of hex behind a fixed <c>Aa1!</c> prefix. The prefix supplies an upper-case letter,
/// a lower-case letter, a digit and a symbol, so SQL Server's complexity rule passes; hex contains no
/// <c>;</c>, quote, <c>=</c> or whitespace, so the value is safe inside a key=value connection string.
/// </remarks>
internal static class BenchmarkSecrets
{
    private const string ComplexityPrefix = "Aa1!";

    internal static string NewPassword() =>
        ComplexityPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}
