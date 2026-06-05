namespace MetaHub.Identification.Hashing;

/// <summary>
/// MD4 message digest (RFC 1320). .NET ships MD5 but not MD4, and ED2K is built on MD4,
/// so a small self-contained implementation is required.
/// </summary>
public static class Md4
{
    public static byte[] Hash(ReadOnlySpan<byte> message)
    {
        uint a = 0x67452301, b = 0xefcdab89, c = 0x98badcfe, d = 0x10325476;

        long messageLenBits = (long)message.Length * 8;

        // Padding: 0x80 then zeros until length ≡ 56 (mod 64), then 8-byte little-endian bit length.
        int paddedLen = ((message.Length + 8) / 64 + 1) * 64;
        Span<byte> buffer = paddedLen <= 1024 ? stackalloc byte[paddedLen] : new byte[paddedLen];
        message.CopyTo(buffer);
        buffer[message.Length] = 0x80;
        BitConverter.TryWriteBytes(buffer[(paddedLen - 8)..], messageLenBits);

        Span<uint> x = stackalloc uint[16];
        for (int offset = 0; offset < paddedLen; offset += 64)
        {
            for (int i = 0; i < 16; i++)
                x[i] = BitConverter.ToUInt32(buffer.Slice(offset + i * 4, 4));

            uint aa = a, bb = b, cc = c, dd = d;

            // Round 1
            foreach (var i in new[] { 0, 4, 8, 12 })
            {
                a = FF(a, b, c, d, x[i], 3);
                d = FF(d, a, b, c, x[i + 1], 7);
                c = FF(c, d, a, b, x[i + 2], 11);
                b = FF(b, c, d, a, x[i + 3], 19);
            }

            // Round 2
            foreach (var i in new[] { 0, 1, 2, 3 })
            {
                a = GG(a, b, c, d, x[i], 3);
                d = GG(d, a, b, c, x[i + 4], 5);
                c = GG(c, d, a, b, x[i + 8], 9);
                b = GG(b, c, d, a, x[i + 12], 13);
            }

            // Round 3
            foreach (var i in new[] { 0, 2, 1, 3 })
            {
                a = HH(a, b, c, d, x[i], 3);
                d = HH(d, a, b, c, x[i + 8], 9);
                c = HH(c, d, a, b, x[i + 4], 11);
                b = HH(b, c, d, a, x[i + 12], 15);
            }

            a += aa; b += bb; c += cc; d += dd;
        }

        var result = new byte[16];
        BitConverter.TryWriteBytes(result.AsSpan(0), a);
        BitConverter.TryWriteBytes(result.AsSpan(4), b);
        BitConverter.TryWriteBytes(result.AsSpan(8), c);
        BitConverter.TryWriteBytes(result.AsSpan(12), d);
        return result;
    }

    private static uint RotateLeft(uint value, int shift) => (value << shift) | (value >> (32 - shift));

    private static uint FF(uint a, uint b, uint c, uint d, uint xk, int s)
        => RotateLeft(a + ((b & c) | (~b & d)) + xk, s);

    private static uint GG(uint a, uint b, uint c, uint d, uint xk, int s)
        => RotateLeft(a + ((b & c) | (b & d) | (c & d)) + xk + 0x5a827999, s);

    private static uint HH(uint a, uint b, uint c, uint d, uint xk, int s)
        => RotateLeft(a + (b ^ c ^ d) + xk + 0x6ed9eba1, s);
}
