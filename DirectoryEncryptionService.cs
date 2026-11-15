using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Elektra.Security;

public enum DirectoryEncryptionStatus
{
    Completed,
    NoFiles,
    DestinationExists,
    IncorrectPassword,
    CoolingDown
}

public sealed record DirectoryEncryptionResult(DirectoryEncryptionStatus Status, int FilesProcessed, TimeSpan CooldownRemaining = default);

public sealed class DirectoryEncryptionService
{
    private const int SaltSize = 16;
    private const int NoncePrefixSize = 4;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int ChunkSize = 1024 * 1024;
    private const int Pbkdf2Iterations = 600_000;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ELKENC01");
    private readonly object _attemptLock = new();
    private int _failedAttempts;
    private DateTimeOffset _cooldownUntil;

    public string? GetProtectedDirectory(string folderName)
    {
        string? path = folderName.ToLowerInvariant() switch
        {
            "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            _ => null
        };

        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
    }

    public TimeSpan GetCooldownRemaining()
    {
        lock (_attemptLock)
            return _cooldownUntil > DateTimeOffset.UtcNow ? _cooldownUntil - DateTimeOffset.UtcNow : TimeSpan.Zero;
    }

    public int GetAttemptsRemaining()
    {
        lock (_attemptLock)
            return 3 - _failedAttempts;
    }

    public async Task<DirectoryEncryptionResult> EncryptAsync(string folderName, string password, CancellationToken ct)
    {
        string? root = GetProtectedDirectory(folderName);
        if (root == null)
            throw new ArgumentException("Encryption is permitted only for Pictures, Videos, Documents, Music, or Downloads.", nameof(folderName));
        ValidatePassword(password);
        ValidateRoot(root);

        bool hasFiles = false;
        foreach (string path in EnumerateFiles(root))
        {
            if (path.EndsWith(".elektra", StringComparison.OrdinalIgnoreCase))
                continue;

            hasFiles = true;
            if (File.Exists(path + ".elektra"))
                return new DirectoryEncryptionResult(DirectoryEncryptionStatus.DestinationExists, 0);
        }
        if (!hasFiles)
            return new DirectoryEncryptionResult(DirectoryEncryptionStatus.NoFiles, 0);

        int completed = 0;
        foreach (string source in EnumerateFiles(root))
        {
            if (source.EndsWith(".elektra", StringComparison.OrdinalIgnoreCase))
                continue;
            ct.ThrowIfCancellationRequested();
            string destination = source + ".elektra";
            if (File.Exists(destination))
                return new DirectoryEncryptionResult(DirectoryEncryptionStatus.DestinationExists, completed);

            string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None, ChunkSize, useAsync: true))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, ChunkSize, useAsync: true))
                {
                    await EncryptFileAsync(input, output, password, ct).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }

                File.Move(temporary, destination);
                try
                {
                    File.Delete(source);
                }
                catch
                {
                    File.Delete(destination);
                    throw;
                }
                completed++;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        return new DirectoryEncryptionResult(DirectoryEncryptionStatus.Completed, completed);
    }

    public async Task<DirectoryEncryptionResult> DecryptAsync(string folderName, string password, CancellationToken ct)
    {
        string? root = GetProtectedDirectory(folderName);
        if (root == null)
            throw new ArgumentException("Decryption is permitted only for Pictures, Videos, Documents, Music, or Downloads.", nameof(folderName));
        ValidatePassword(password);
        ValidateRoot(root);

        TimeSpan cooldown = GetCooldownRemaining();
        if (cooldown > TimeSpan.Zero)
            return new DirectoryEncryptionResult(DirectoryEncryptionStatus.CoolingDown, 0, cooldown);

        bool hasFiles = false;
        foreach (string path in EnumerateFiles(root))
        {
            if (!path.EndsWith(".elektra", StringComparison.OrdinalIgnoreCase))
                continue;

            hasFiles = true;
            if (File.Exists(path[..^".elektra".Length]))
                return new DirectoryEncryptionResult(DirectoryEncryptionStatus.DestinationExists, 0);
        }
        if (!hasFiles)
            return new DirectoryEncryptionResult(DirectoryEncryptionStatus.NoFiles, 0);

        try
        {
            foreach (string file in EnumerateFiles(root))
            {
                if (!file.EndsWith(".elektra", StringComparison.OrdinalIgnoreCase))
                    continue;
                ct.ThrowIfCancellationRequested();
                await using var validationInput = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true);
                await DecryptFileAsync(validationInput, Stream.Null, password, ct).ConfigureAwait(false);
            }
        }
        catch (CryptographicException)
        {
            TimeSpan remaining = RecordFailedAttempt();
            return remaining > TimeSpan.Zero
                ? new DirectoryEncryptionResult(DirectoryEncryptionStatus.CoolingDown, 0, remaining)
                : new DirectoryEncryptionResult(DirectoryEncryptionStatus.IncorrectPassword, 0);
        }

        lock (_attemptLock)
        {
            _failedAttempts = 0;
            _cooldownUntil = DateTimeOffset.MinValue;
        }

        int completed = 0;
        foreach (string source in EnumerateFiles(root))
        {
            if (!source.EndsWith(".elektra", StringComparison.OrdinalIgnoreCase))
                continue;
            ct.ThrowIfCancellationRequested();
            string destination = source[..^".elektra".Length];
            if (File.Exists(destination))
                return new DirectoryEncryptionResult(DirectoryEncryptionStatus.DestinationExists, completed);

            string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, ChunkSize, useAsync: true))
                {
                    await DecryptFileAsync(input, output, password, ct).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }

                File.Move(temporary, destination);
                try
                {
                    File.Delete(source);
                }
                catch
                {
                    File.Delete(destination);
                    throw;
                }
                completed++;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        return new DirectoryEncryptionResult(DirectoryEncryptionStatus.Completed, completed);
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            string[] files = Directory.EnumerateFiles(current).ToArray();
            foreach (string file in files)
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                    yield return file;
            }

            foreach (string directory in Directory.EnumerateDirectories(current))
            {
                FileAttributes attributes = File.GetAttributes(directory);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory)
                    pending.Push(directory);
            }
        }
    }

    private static void ValidateRoot(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"The protected folder does not exist: {root}");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The selected protected folder is a link or mount point and cannot be processed safely.");
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ArgumentException("Use a password of at least eight characters.", nameof(password));
    }

    private static async Task EncryptFileAsync(Stream input, Stream output, string password, CancellationToken ct)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixSize);
        byte[] header = [.. Magic, .. salt, .. noncePrefix];
        await output.WriteAsync(header, ct).ConfigureAwait(false);

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        byte[] plain = [];
        byte[] cipher = [];
        byte[] tag = [];
        byte[] aad = [];
        byte[] lengthBytes = [];
        byte[] nonce = [];
        try
        {
            plain = ArrayPool<byte>.Shared.Rent(ChunkSize);
            cipher = ArrayPool<byte>.Shared.Rent(ChunkSize);
            tag = new byte[TagSize];
            aad = new byte[header.Length + sizeof(long) + sizeof(int)];
            lengthBytes = new byte[sizeof(int)];
            nonce = new byte[NonceSize];
            header.CopyTo(aad, 0);
            noncePrefix.CopyTo(nonce, 0);

            using var aes = new AesGcm(key, TagSize);
            long counter = 0;
            while (true)
            {
                int length = await ReadChunkAsync(input, plain, ct).ConfigureAwait(false);
                BinaryPrimitives.WriteInt32LittleEndian(aad.AsSpan(header.Length + sizeof(long)), length);
                BinaryPrimitives.WriteInt64LittleEndian(aad.AsSpan(header.Length), counter);
                BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, length);
                BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(NoncePrefixSize), counter);
                await output.WriteAsync(lengthBytes, ct).ConfigureAwait(false);
                if (length == 0)
                {
                    aes.Encrypt(nonce, ReadOnlySpan<byte>.Empty, Span<byte>.Empty, tag, aad);
                    await output.WriteAsync(tag, ct).ConfigureAwait(false);
                    break;
                }

                aes.Encrypt(nonce, plain.AsSpan(0, length), cipher.AsSpan(0, length), tag, aad);
                await output.WriteAsync(cipher.AsMemory(0, length), ct).ConfigureAwait(false);
                await output.WriteAsync(tag, ct).ConfigureAwait(false);
                counter++;
            }
            await output.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(cipher);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(aad);
            CryptographicOperations.ZeroMemory(lengthBytes);
            CryptographicOperations.ZeroMemory(nonce);
            if (plain.Length > 0)
                ArrayPool<byte>.Shared.Return(plain);
            if (cipher.Length > 0)
                ArrayPool<byte>.Shared.Return(cipher);
        }
    }

    private static async Task DecryptFileAsync(Stream input, Stream output, string password, CancellationToken ct)
    {
        byte[] header = new byte[Magic.Length + SaltSize + NoncePrefixSize];
        await input.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("The file is not a supported Elektra-encrypted file.");

        byte[] salt = header.AsSpan(Magic.Length, SaltSize).ToArray();
        byte[] noncePrefix = header.AsSpan(Magic.Length + SaltSize, NoncePrefixSize).ToArray();
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        byte[] cipher = [];
        byte[] plain = [];
        byte[] tag = [];
        byte[] aad = [];
        byte[] lengthBytes = [];
        byte[] nonce = [];
        try
        {
            cipher = ArrayPool<byte>.Shared.Rent(ChunkSize);
            plain = ArrayPool<byte>.Shared.Rent(ChunkSize);
            tag = new byte[TagSize];
            aad = new byte[header.Length + sizeof(long) + sizeof(int)];
            lengthBytes = new byte[sizeof(int)];
            nonce = new byte[NonceSize];
            noncePrefix.CopyTo(nonce, 0);
            header.CopyTo(aad, 0);

            using var aes = new AesGcm(key, TagSize);
            long counter = 0;
            while (true)
            {
                await input.ReadExactlyAsync(lengthBytes, ct).ConfigureAwait(false);
                BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(NoncePrefixSize), counter);
                int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
                if (length < 0 || length > ChunkSize)
                    throw new InvalidDataException("The encrypted file contains an invalid chunk length.");

                BinaryPrimitives.WriteInt64LittleEndian(aad.AsSpan(header.Length), counter);
                BinaryPrimitives.WriteInt32LittleEndian(aad.AsSpan(header.Length + sizeof(long)), length);
                if (length == 0)
                {
                    await input.ReadExactlyAsync(tag, ct).ConfigureAwait(false);
                    aes.Decrypt(nonce, ReadOnlySpan<byte>.Empty, tag, Span<byte>.Empty, aad);
                    if (input.ReadByte() != -1)
                        throw new InvalidDataException("The encrypted file contains unexpected trailing data.");
                    break;
                }

                await input.ReadExactlyAsync(cipher.AsMemory(0, length), ct).ConfigureAwait(false);
                await input.ReadExactlyAsync(tag, ct).ConfigureAwait(false);
                aes.Decrypt(nonce, cipher.AsSpan(0, length), tag, plain.AsSpan(0, length), aad);
                await output.WriteAsync(plain.AsMemory(0, length), ct).ConfigureAwait(false);
                counter++;
            }
            await output.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(cipher);
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(aad);
            CryptographicOperations.ZeroMemory(lengthBytes);
            CryptographicOperations.ZeroMemory(nonce);
            if (cipher.Length > 0)
                ArrayPool<byte>.Shared.Return(cipher);
            if (plain.Length > 0)
                ArrayPool<byte>.Shared.Return(plain);
        }
    }

    private static async Task<int> ReadChunkAsync(Stream input, byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < ChunkSize)
        {
            int read = await input.ReadAsync(buffer.AsMemory(total, ChunkSize - total), ct).ConfigureAwait(false);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    private TimeSpan RecordFailedAttempt()
    {
        lock (_attemptLock)
        {
            _failedAttempts++;
            if (_failedAttempts < 3)
                return TimeSpan.Zero;

            _failedAttempts = 0;
            _cooldownUntil = DateTimeOffset.UtcNow.AddSeconds(30);
            return TimeSpan.FromSeconds(30);
        }
    }
}
