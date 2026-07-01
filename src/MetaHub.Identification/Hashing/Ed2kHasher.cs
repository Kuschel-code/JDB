namespace MetaHub.Identification.Hashing;

/// <summary>
/// Computes the ED2K hash used by AniDB to identify individual release files.
///
/// Algorithm: split the file into 9,728,000-byte chunks, MD4 each chunk, then MD4 the
/// concatenation of the chunk hashes. A file that fits in a single chunk hashes to the
/// MD4 of its bytes directly. A file whose size is an exact multiple of the chunk size
/// additionally hashes an empty trailing chunk (AniDB "red" variant).
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
        bool endedOnChunkBoundary = false;

        while (true)
        {
            int read = await ReadFullChunkAsync(stream, buffer, ct);
            if (read == 0 && chunkHashes.Count > 0)
            {
                endedOnChunkBoundary = true;
                break;
            }

            chunkHashes.Add(Md4.Hash(buffer.AsSpan(0, read)));

            if (read < ChunkSize)
                break;
        }

        // AniDB "red" variant: a file that ends exactly on a chunk boundary gets one extra
        // MD4-of-empty chunk appended before the final hash. This matches AniDB's canonical
        // <ed2k>; omitting it (the "blue" variant) only matches the secondary <ed2k_alt> and so
        // fails the primary FILE lookup. (Verified against the published 0x55*ChunkSize vector.)
        if (endedOnChunkBoundary)
            chunkHashes.Add(Md4.Hash(ReadOnlySpan<byte>.Empty));

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
