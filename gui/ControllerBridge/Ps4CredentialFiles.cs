using System.Security.Cryptography;

namespace ControllerBridge;

// File-format validation only. Credentials never enter device settings or logs.
internal static class Ps4CredentialFiles
{
    internal static void Validate(string privateKeyPath, string serialPath, string signaturePath)
    {
        ValidatePrivateKey(privateKeyPath);
        byte[] serial = ReadBounded(serialPath, 1024, L.T("シリアル番号", "Serial number"));
        try
        {
            int digits = 0;
            foreach (byte value in serial)
            {
                if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') continue;
                if (!(value is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f' or >= (byte)'A' and <= (byte)'F'))
                    throw new InvalidDataException(L.T("シリアル番号は32桁の16進文字列で指定してください。", "The serial number must contain 32 hexadecimal digits."));
                digits++;
            }
            if (digits != 32) throw new InvalidDataException(L.T("シリアル番号は16バイト分（32桁）の16進文字列が必要です。", "The serial number requires 16 bytes encoded as 32 hexadecimal digits."));
        }
        finally { CryptographicOperations.ZeroMemory(serial); }

        byte[] signature = ReadBounded(signaturePath, 257, L.T("機器署名", "Device signature"));
        try
        {
            if (signature.Length != 256) throw new InvalidDataException(L.T("機器署名は256バイトのバイナリファイルが必要です。", "The device signature must be a 256-byte binary file."));
        }
        finally { CryptographicOperations.ZeroMemory(signature); }
    }

    private static void ValidatePrivateKey(string path)
    {
        byte[] bytes = ReadBounded(path, 16384, L.T("秘密鍵", "Private key"));
        char[] pem = new char[bytes.Length];
        try
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] > 127) throw new FormatException();
                pem[i] = (char)bytes[i];
            }
            ReadOnlySpan<char> text = pem;
            if (!PemEncoding.TryFind(text, out PemFields fields)) throw new FormatException();
            ReadOnlySpan<char> label = text[fields.Label];
            if (!label.SequenceEqual("RSA PRIVATE KEY") && !label.SequenceEqual("PRIVATE KEY")) throw new FormatException();
            // A single unencrypted PKCS#1 or PKCS#8 private key is required.
            using RSA key = RSA.Create();
            key.ImportFromPem(text);
            if (key.KeySize != 2048)
                throw new InvalidDataException(L.T("RSA 2048ビットの秘密鍵が必要です。", "An RSA 2048-bit private key is required."));
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
        {
            // Do not expose parser exceptions or key contents in UI/logging.
            throw new InvalidDataException(L.T("秘密鍵を読み込めません。暗号化されていないRSA秘密鍵のPEMファイルを指定してください。",
                "Cannot read the private key. Choose an unencrypted RSA private key in PEM format."));
        }
        finally { Array.Clear(pem); CryptographicOperations.ZeroMemory(bytes); }
    }

    private static byte[] ReadBounded(string path, int maximum, string label)
    {
        byte[]? data = null;
        try
        {
            using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is <= 0 || file.Length > maximum) throw new IOException();
            data = new byte[(int)file.Length];
            file.ReadExactly(data);
            if (file.ReadByte() != -1) throw new IOException();
            return data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            if (data is not null) CryptographicOperations.ZeroMemory(data);
            throw new InvalidDataException(L.F("{0}ファイルを読み込めません。場所・アクセス権・ファイルサイズを確認してください。",
                "Cannot read the {0} file. Check its location, access permissions and file size.", label));
        }
    }
}
