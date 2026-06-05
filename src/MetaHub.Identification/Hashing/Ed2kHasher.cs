namespace MetaHub.Identification.Hashing;

/// <summary>
/// Computes the ED2K hash used by AniDB to identify individual release files.
///
/// Algorithm: split the file into 9,728,000-byte chunks, MD4 each chunk, then MD4 the
/// concatenation of the chunk hashes. A file that fits in a single chunk hashes to the
/// MD4 of its bytes directly.
/// </summary>
public static class Ed2kHasher
{
    public const int ChunkSize = 9_728_000;

    /// <summary>
    /// Streams <paramref name="stream"/> and returns the lowercase hex ED2K hash.
    /// Reads one chunk at a time to keep memory bounded for large files.
    /// </summary>
    public static async Task<string> ComputeAsync(Stream stream, CancellationToken ct = default)
    {
        var chunkHashes = new List<byte[]>();
        var buffer = new byte[ChunkSize];

        while (true)
        {
            int read = await ReadFullChunkAsync(stream, buffer, ct);
            if (read == 0 && chunkHashes.Count > 0)
                break; // whole file already consumed on a chunk boundary

            chunkHashes.Add(Md4.Hash(buffer.AsSpan(0, read)));

            if (read < ChunkSize)
                break; // last (partial) chunk
        }

        // Single chunk → the ED2K hash is just that chunk's MD4 (also covers the empty file).
        byte[] digest = chunkHashes.Count == 1
            ? chunkHashes[0]
            : Md4.Hash(Concat(chunkHashes));

        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    public static async Task<string> ComputeFileAsync(string path, CancellationToken ct = default)
    {
        await using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: true);
        return await ComputeAsync(fs, ct);
    }

    private static async Task<int> ReadFullChunkAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private static byte[] Concat(List<byte[]> parts)
    {
        var result = new byte[parts.Count * 16];
        for (int i = 0; i < parts.Count; i++)
            Array.Copy(parts[i], 0, result, i * 16, 16);
        return result;
    }
}
