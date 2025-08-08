#region

using System.Security.Cryptography;
using System.Text;

#endregion

namespace pengdow.crud;

public sealed class EphemeralSecureString : IEphemeralSecureString, IDisposable
{
    private const int TTL_MS = 750;
    private readonly byte[] _cipherText;
    private readonly Encoding _encoding;
    private readonly byte[] _iv;
    private readonly byte[] _key;
    private readonly object _lock = new();

    private byte[]? _cachedPlainBytes;
    private long _disposed;
    private Timer? _timer;

    public EphemeralSecureString(string input)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));

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
        if (Interlocked.Read(ref _disposed) == 1) throw new ObjectDisposedException(nameof(EphemeralSecureString));

        lock (_lock)
        {
            if (_cachedPlainBytes == null) _cachedPlainBytes = DecryptBytes(_cipherText, _key, _iv);

            _timer?.Dispose();
            _timer = new Timer(ClearPlainText, null, TTL_MS, Timeout.Infinite);

            return _encoding.GetString(_cachedPlainBytes);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            ClearPlainText(null);
            CryptographicOperations.ZeroMemory(_key);
            CryptographicOperations.ZeroMemory(_iv);
            CryptographicOperations.ZeroMemory(_cipherText);
        }
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