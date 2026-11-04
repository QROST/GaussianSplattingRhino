using System.Diagnostics;
using System.Security.Cryptography;

namespace GSRhino.Core;

/// <summary>Publishes verified content-addressed files. Call from background work; corrupt targets are retained as backups.</summary>
public static class GaussianCachePublisher
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Consumes preparedPath only when publishing succeeds; the caller owns cleanup otherwise.
    /// Writers using this helper serialize across processes. A valid destination is never replaced.
    /// Cancellation is honored before the atomic commit, never reported after a successful commit.
    /// </summary>
    public static bool Publish(string preparedPath, string destinationPath, string expectedHash, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (expectedHash.Length != 64 || expectedHash.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("Invalid cache SHA-256.", nameof(expectedHash));
        expectedHash = expectedHash.ToLowerInvariant();
        string prepared = Path.GetFullPath(preparedPath), destination = Path.GetFullPath(destinationPath);
        if (Digest(prepared, ct) != expectedHash) throw new IOException("待恢复资源的 SHA-256 不匹配；现有缓存和源文件未修改。");
        if (string.Equals(prepared, destination, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return false;

        using FileStream gate = AcquireLock(destination, ct);
        // Another importer may have repaired/published the same asset while we waited.
        if (File.Exists(destination) && Digest(destination, ct) == expectedHash) return false;
        ct.ThrowIfCancellationRequested();
        if (File.Exists(destination))
        {
            string backup = destination + ".corrupt-" + Guid.NewGuid().ToString("N");
            // Same-volume replacement has no missing-target interval. The old bytes remain available for diagnosis.
            File.Replace(prepared, destination, backup);
        }
        else File.Move(prepared, destination);
        return true;
    }

    private static FileStream AcquireLock(string destination, CancellationToken ct)
    {
        string directory = Path.Combine(Path.GetDirectoryName(destination)!, ".locks");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string path = Path.Combine(directory, Path.GetFileName(destination) + ".lock");
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, options); }
            catch (IOException) when (File.Exists(path))
            {
                if (elapsed.Elapsed >= LockTimeout) throw new IOException("等待其他 GS 缓存操作超时；现有缓存未修改，请稍后重试。");
                if (ct.WaitHandle.WaitOne(50)) ct.ThrowIfCancellationRequested();
            }
        }
        // The lock file is intentionally retained: removing it can let waiting writers lock different file identities.
    }

    private static string Digest(string path, CancellationToken ct)
    {
        // Permit atomic cache replacement while a reader finishes hashing the previous file.
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[131072]; int count;
        while ((count = input.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        ct.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
