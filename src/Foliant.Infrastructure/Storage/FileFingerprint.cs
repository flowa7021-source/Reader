using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Foliant.Application.Services;

namespace Foliant.Infrastructure.Storage;

public sealed class FileFingerprint : IFileFingerprint
{
    private const int HeadBytes = 64 * 1024;

    public async Task<string> ComputeAsync(string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException(null, path);
        }

        var buffer = ArrayPool<byte>.Shared.Rent(HeadBytes);
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: HeadBytes,
                useAsync: true);

            var read = await ReadFullyAsync(stream, buffer, ct).ConfigureAwait(false);

            using var sha = SHA256.Create();
            sha.TransformBlock(buffer, 0, read, null, 0);

            var tail = BuildTail(info.Length, info.LastWriteTimeUtc);
            sha.TransformFinalBlock(tail, 0, tail.Length);

            return Convert.ToHexStringLower(sha.Hash!);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }
            total += n;
        }
        return total;
    }

    private static byte[] BuildTail(long size, DateTime lastWriteUtc)
    {
        var s = string.Create(
            CultureInfo.InvariantCulture,
            $"|{size}|{lastWriteUtc.Ticks}");
        return Encoding.UTF8.GetBytes(s);
    }
}
