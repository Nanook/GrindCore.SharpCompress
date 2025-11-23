using System;
using System.Security.Cryptography;

namespace SharpCompress.Common.Zip;

internal class WinzipAesEncryptionData
{
    private const int RFC2898_ITERATIONS = 1000;

    private readonly WinzipAesKeySize _keySize;

    internal WinzipAesEncryptionData(
        WinzipAesKeySize keySize,
        byte[] salt,
        byte[] passwordVerifyValue,
        string password
    )
    {
        _keySize = keySize;

        int keySizeBytes = KeySizeInBytes;
        int totalBytes = (keySizeBytes * 2) + 2; // key + iv + verify
#if NET6_0_OR_GREATER
        // Modern frameworks: derive all bytes in one call
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            RFC2898_ITERATIONS,
            HashAlgorithmName.SHA1,
            totalBytes
        );
#else
        // Older frameworks: fall back to instance-based derivation
#pragma warning disable SYSLIB0060
        using var rfc2898 = new Rfc2898DeriveBytes(password, salt, RFC2898_ITERATIONS);
        var derived = rfc2898.GetBytes(totalBytes);
#pragma warning restore SYSLIB0060
#endif

        KeyBytes = new byte[keySizeBytes];
        IvBytes = new byte[keySizeBytes];
        var verifyBytes = new byte[2];

        Array.Copy(derived, 0, KeyBytes, 0, keySizeBytes);
        Array.Copy(derived, keySizeBytes, IvBytes, 0, keySizeBytes);
        Array.Copy(derived, keySizeBytes * 2, verifyBytes, 0, 2);

        short expected = ReadInt16LittleEndian(passwordVerifyValue);
        short actual = ReadInt16LittleEndian(verifyBytes);

        if (expected != actual)
        {
            throw new InvalidFormatException("bad password");
        }
    }

    internal byte[] IvBytes { get; set; }

    internal byte[] KeyBytes { get; set; }

    private int KeySizeInBytes => KeyLengthInBytes(_keySize);

    internal static int KeyLengthInBytes(WinzipAesKeySize keySize) =>
        keySize switch
        {
            WinzipAesKeySize.KeySize128 => 16,
            WinzipAesKeySize.KeySize192 => 24,
            WinzipAesKeySize.KeySize256 => 32,
            _ => throw new InvalidOperationException(),
        };

    private static short ReadInt16LittleEndian(byte[] data)
    {
        if (data == null || data.Length < 2)
        {
            throw new ArgumentException("Data must be at least 2 bytes long", nameof(data));
        }
        return (short)(data[0] | (data[1] << 8));
    }
}
