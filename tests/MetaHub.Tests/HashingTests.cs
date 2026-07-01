using System.Text;
using MetaHub.Identification.Hashing;
using Xunit;

namespace MetaHub.Tests;

public class HashingTests
{
    // RFC 1320 MD4 test vectors.
    [Theory]
    [InlineData("", "31d6cfe0d16ae931b73c59d7e0c089c0")]
    [InlineData("a", "bde52cb31de33e46245e05fbdbd6fb24")]
    [InlineData("abc", "a448017aaf21d8525fc10ae87aa6729d")]
    [InlineData("message digest", "d9130a8164549fe818874806e1c7014b")]
    [InlineData("abcdefghijklmnopqrstuvwxyz", "d79e1c308aa5bbcdeea8ed63df412da9")]
    public void Md4_matches_rfc1320_vectors(string input, string expected)
    {
        var hash = Convert.ToHexString(Md4.Hash(Encoding.ASCII.GetBytes(input))).ToLowerInvariant();
        Assert.Equal(expected, hash);
    }

    [Fact]
    public async Task Ed2k_of_empty_file_is_md4_of_empty()
    {
        using var stream = new MemoryStream(Array.Empty<byte>());
        var hash = await Ed2kHasher.ComputeAsync(stream);
        Assert.Equal("31d6cfe0d16ae931b73c59d7e0c089c0", hash);
    }

    [Fact]
    public async Task Ed2k_of_small_data_equals_md4_of_data()
    {
        // For data smaller than one chunk, ED2K == MD4 of the bytes.
        var data = Encoding.ASCII.GetBytes("abc");
        using var stream = new MemoryStream(data);
        var hash = await Ed2kHasher.ComputeAsync(stream);
        Assert.Equal("a448017aaf21d8525fc10ae87aa6729d", hash);
    }

    [Fact]
    public async Task Ed2k_multi_chunk_equals_md4_of_concatenated_chunk_hashes()
    {
        // The real path for any actual anime file (>= one 9,728,000-byte chunk) was never exercised.
        // Validate the chunking against MD4(MD4(chunk0) || MD4(chunk1)) using the RFC-verified Md4.
        const int chunk = Ed2kHasher.ChunkSize;
        var data = new byte[chunk + 12_345];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)((i * 131 + 7) & 0xFF);

        var concat = new byte[32];
        Md4.Hash(data.AsSpan(0, chunk)).CopyTo(concat.AsSpan(0));
        Md4.Hash(data.AsSpan(chunk)).CopyTo(concat.AsSpan(16));
        var expected = Convert.ToHexString(Md4.Hash(concat)).ToLowerInvariant();

        using var stream = new MemoryStream(data);
        var hash = await Ed2kHasher.ComputeAsync(stream);

        Assert.Equal(expected, hash);
        // And it must NOT collapse to a single-chunk MD4 of the whole buffer.
        Assert.NotEqual(Convert.ToHexString(Md4.Hash(data)).ToLowerInvariant(), hash);
    }

    [Fact]
    public async Task Ed2k_exact_one_chunk_matches_published_AniDB_red_vector()
    {
        // External, independently published vector (github.com/Kimundi/ed2k): exactly one
        // 9,728,000-byte chunk of 0x55. AniDB's canonical hash is the "red" variant (extra empty
        // trailing chunk). This pins correctness against a third-party reference, not our own MD4.
        var data = new byte[Ed2kHasher.ChunkSize];
        Array.Fill(data, (byte)0x55);
        using var stream = new MemoryStream(data);

        Assert.Equal("49e80f377b7e4e706dbd3ecc89f39306", await Ed2kHasher.ComputeAsync(stream));
        Assert.NotEqual("4127a47867b6110f0f86f2d9845fb374", await Ed2kHasher.ComputeAsync(new MemoryStream(data)));
    }

    [Fact]
    public async Task Ed2k_exact_two_chunks_appends_empty_trailing_chunk()
    {
        const int chunk = Ed2kHasher.ChunkSize;
        var data = new byte[2 * chunk];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)(i & 0xFF);

        // red = MD4( MD4(c0) || MD4(c1) || MD4(empty) )
        var concat = new byte[48];
        Md4.Hash(data.AsSpan(0, chunk)).CopyTo(concat.AsSpan(0));
        Md4.Hash(data.AsSpan(chunk, chunk)).CopyTo(concat.AsSpan(16));
        Md4.Hash(ReadOnlySpan<byte>.Empty).CopyTo(concat.AsSpan(32));
        var expected = Convert.ToHexString(Md4.Hash(concat)).ToLowerInvariant();

        using var stream = new MemoryStream(data);
        Assert.Equal(expected, await Ed2kHasher.ComputeAsync(stream));
    }

    [Fact]
    public async Task Crc32_known_value()
    {
        // CRC32 of "123456789" is the standard check value 0xCBF43926.
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("123456789"));
            var crc = await Crc32.ComputeFileAsync(path);
            Assert.Equal("cbf43926", crc);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
