// =============================================================================
// FILE: EphemeralSecureString.cs
// PURPOSE: Provides short-lived, encrypted in-memory storage for sensitive
//          strings like connection string passwords or API keys.
//
// AI SUMMARY:
// - This class encrypts sensitive strings in memory using AES encryption.
// - The plaintext is only decrypted when Reveal() is called and is
//   automatically cleared from memory after 750ms (TTL_MS).
// - Designed to minimize the window during which sensitive data is exposed
//   in plaintext in process memory.
// - Uses CryptographicOperations.ZeroMemory to securely clear byte arrays.
// - Implements IDisposable/IAsyncDisposable via SafeAsyncDisposableBase.
// - Use case: Store database passwords in memory without leaving them
//   as plaintext strings that could be captured in memory dumps.
// - The WithRevealed() method provides a callback pattern for using the
//   string without storing it in a variable.
// =============================================================================

#region

using System.Security.Cryptography;
using System.Text;
using pengdows.crud.infrastructure;

#endregion

namespace pengdows.crud;

/// <summary>
/// Provides secure, short-lived storage for sensitive strings with automatic memory clearing.
/// </summary>
/// <remarks>
/// <para>
/// This class addresses the challenge of storing sensitive data like passwords or API keys
/// in memory. Unlike <see cref="System.Security.SecureString"/> (which is deprecated on
/// .NET Core), this implementation uses AES encryption to keep the data secure in memory.
/// </para>
/// <para>
/// <strong>Security Model:</strong>
/// </para>
/// <list type="bullet">
/// <item><description>The input string is immediately encrypted using a randomly generated AES key.</description></item>
/// <item><description>The key and IV are stored in memory (protected by runtime only).</description></item>
/// <item><description>When <see cref="Reveal"/> is called, the plaintext is decrypted and cached.</description></item>
/// <item><description>The plaintext cache is automatically cleared after 750ms.</description></item>
/// <item><description>On disposal, all byte arrays are securely zeroed.</description></item>
/// </list>
/// <para>
/// <strong>Limitations:</strong> This provides defense-in-depth but cannot prevent
/// a determined attacker with debugger access. The primary goal is to reduce the
/// window of exposure and prevent sensitive strings from appearing in crash dumps.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Store a password securely
/// using var securePassword = new EphemeralSecureString(passwordFromUser);
///
/// // Later, when building a connection string:
/// securePassword.WithRevealed(password =>
/// {
///     connectionStringBuilder.Password = password;
/// });
/// // Password is cleared from memory after 750ms
/// </code>
/// </example>
/// <seealso cref="IEphemeralSecureString"/>
public sealed class EphemeralSecureString : SafeAsyncDisposableBase, IEphemeralSecureString
{
    /// <summary>
    /// Time-to-live in milliseconds before the cached plaintext is cleared.
    /// </summary>
    private const int TTL_MS = 750;
    private readonly byte[] _cipherText;
    private readonly Encoding _encoding;
    private readonly byte[] _iv;
    private readonly byte[] _key;
    private readonly object _lock = new();

    private byte[]? _cachedPlainBytes;
    private Timer? _timer;

    public EphemeralSecureString(string input)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        _encoding = Encoding.UTF8;

        using var aes = Aes.Create();
        aes.GenerateKey();
        aes.GenerateIV();

        _key = aes.Key;
        _iv = aes.IV;
        _cipherText = EncryptStringToBytes(input, _key, _iv, _encoding);
    }

    public string Reveal()
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            if (_cachedPlainBytes == null)
            {
                _cachedPlainBytes = DecryptBytes(_cipherText, _key, _iv);
            }

            _timer?.Dispose();
            _timer = new Timer(ClearPlainText, null, TTL_MS, Timeout.Infinite);

            return _encoding.GetString(_cachedPlainBytes);
        }
    }

    protected override void DisposeManaged()
    {
        ClearPlainText(null);
        CryptographicOperations.ZeroMemory(_key);
        CryptographicOperations.ZeroMemory(_iv);
        CryptographicOperations.ZeroMemory(_cipherText);
    }

    public void WithRevealed(Action<string> use)
    {
        var plain = Reveal();
        use(plain);
    }

    private void ClearPlainText(object? _)
    {
        lock (_lock)
        {
            if (_cachedPlainBytes != null)
            {
                CryptographicOperations.ZeroMemory(_cachedPlainBytes);
                _cachedPlainBytes = null;
            }

            _timer?.Dispose();
            _timer = null;
        }
    }

    private static byte[] EncryptStringToBytes(string plainText, byte[] key, byte[] iv, Encoding encoding)
    {
        using var aes = Aes.Create();
        using var encryptor = aes.CreateEncryptor(key, iv);
        using var ms = new MemoryStream();
        using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
        var encodingNoBom = encoding is UTF8Encoding ? new UTF8Encoding(false) : encoding;
        using var sw = new StreamWriter(cs, encodingNoBom);
        sw.Write(plainText);
        sw.Flush();
        cs.FlushFinalBlock();
        return ms.ToArray();
    }

    private byte[] DecryptBytes(byte[] cipherText, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        using var decryptor = aes.CreateDecryptor(key, iv);
        using var ms = new MemoryStream(cipherText);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var msOut = new MemoryStream();
        cs.CopyTo(msOut);
        return msOut.ToArray();
    }
}